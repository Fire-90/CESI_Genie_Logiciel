using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasySave.Models;
using EasyLog;

namespace EasySave.Services
{
    public class BackupEngine
    {
        public delegate void ProgressUpdateHandler(string currentFile, int remainingFiles);
        public event ProgressUpdateHandler OnProgressUpdate;
        public event Action<string, int> OnJobProgress;
        public event Action<string, bool> OnJobWaiting;
        public event Action<string, bool> OnJobBlocked;

        private readonly StateTracker _stateTracker;
        private readonly ConfigManager _configManager;
        private readonly object _stateLock = new object();

        private static int GlobalPriorityFilesCount = 0;

        private static readonly SemaphoreSlim _cryptoSoftLock = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim _largeFileLock = new SemaphoreSlim(1, 1);

        private readonly ConcurrentDictionary<string, ManualResetEvent> _jobPauseEvents = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _jobCancellationTokens = new();

        public BackupEngine(StateTracker stateTracker, ConfigManager configManager)
        {
            _stateTracker = stateTracker;
            _configManager = configManager;
        }

        public void PauseJob(string jobName)
        {
            if (_jobPauseEvents.TryGetValue(jobName, out var pauseEvent)) pauseEvent.Reset();
        }

        public void ResumeJob(string jobName)
        {
            if (_jobPauseEvents.TryGetValue(jobName, out var pauseEvent)) pauseEvent.Set();
        }

        public void StopJob(string jobName)
        {
            if (_jobCancellationTokens.TryGetValue(jobName, out var cts)) cts.Cancel();
            ResumeJob(jobName);
        }

        private string GetRunningBusinessSoftware()
        {
            var settings = _configManager.LoadSettings();
            if (settings.BusinessSoftwares == null) return null;

            foreach (string software in settings.BusinessSoftwares)
            {
                if (string.IsNullOrWhiteSpace(software)) continue;

                Process[] processes = Process.GetProcessesByName(software);
                if (processes.Length > 0) return software;
            }
            return null;
        }

        public async Task ExecuteJobAsync(BackupJob job)
        {
            string blocking = GetRunningBusinessSoftware();
            if (blocking != null) throw new InvalidOperationException($"BLOCKING|{blocking}");

            if (!Directory.Exists(job.SourceDirectory)) throw new DirectoryNotFoundException("Source directory not found.");

            var cts = new CancellationTokenSource();
            var pauseEvent = new ManualResetEvent(true);
            _jobCancellationTokens[job.Name] = cts;
            _jobPauseEvents[job.Name] = pauseEvent;

            string[] allFiles = Directory.GetFiles(job.SourceDirectory, "*.*", SearchOption.AllDirectories);
            var settings = _configManager.LoadSettings();
            List<string> priorityExtensions = settings.PriorityExtensions ?? new List<string>();

            var priorityFiles = new List<string>();
            var normalFiles = new List<string>();

            foreach (string file in allFiles)
            {
                string extension = Path.GetExtension(file).ToLower();
                if (priorityExtensions.Contains(extension)) priorityFiles.Add(file);
                else normalFiles.Add(file);
            }

            int totalFilesToCopy = priorityFiles.Count + normalFiles.Count;
            long totalFilesSize = allFiles.Sum(f => new FileInfo(f).Length);

            int remainingPriorityFiles = priorityFiles.Count;
            Interlocked.Add(ref GlobalPriorityFilesCount, remainingPriorityFiles);

            lock (_stateLock)
            {
                _stateTracker.UpdateState(job.Name, s =>
                {
                    s.State = "ACTIVE";
                    s.TotalFilesToCopy = totalFilesToCopy;
                    s.TotalFilesSize = totalFilesSize;
                    s.NbFilesLeftToDo = totalFilesToCopy;
                    s.RemainingFilesSize = totalFilesSize;
                    s.Progression = 0;
                });
            }

            try
            {
                foreach (string file in priorityFiles)
                {
                    pauseEvent.WaitOne();
                    cts.Token.ThrowIfCancellationRequested();

                    try
                    {
                        await ProcessSingleFileAsync(file, job);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref GlobalPriorityFilesCount);
                        remainingPriorityFiles--;
                    }
                }

                foreach (string file in normalFiles)
                {
                    while (Interlocked.CompareExchange(ref GlobalPriorityFilesCount, 0, 0) > 0)
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        await Task.Delay(200);
                    }

                    pauseEvent.WaitOne();
                    cts.Token.ThrowIfCancellationRequested();

                    await ProcessSingleFileAsync(file, job);
                }
            }
            catch (OperationCanceledException)
            {
                throw new Exception("Job stopped manually.");
            }
            finally
            {
                // Libère la barrière au cas où un fichier a causé un crash 
                if (remainingPriorityFiles > 0)
                {
                    Interlocked.Add(ref GlobalPriorityFilesCount, -remainingPriorityFiles);
                }

                _jobCancellationTokens.TryRemove(job.Name, out _);
                _jobPauseEvents.TryRemove(job.Name, out _);

                lock (_stateLock)
                {
                    _stateTracker.UpdateState(job.Name, s =>
                    {
                        s.State = "END";
                        s.SourceFilePath = "";
                        s.TargetFilePath = "";
                        s.TotalFilesToCopy = 0;
                        s.TotalFilesSize = 0;
                        s.NbFilesLeftToDo = 0;
                        s.Progression = 0;
                    });
                }
                OnJobProgress?.Invoke(job.Name, 100);
            }
        }

        private async Task ProcessSingleFileAsync(string file, BackupJob job)
        {
            string currentBlockingSoftware = GetRunningBusinessSoftware();
            if (currentBlockingSoftware != null) throw new InvalidOperationException($"BLOCKING|{currentBlockingSoftware}");

            string relativePath = Path.GetRelativePath(job.SourceDirectory, file);
            string targetFile = Path.Combine(job.TargetDirectory, relativePath);

            string targetFileDir = Path.GetDirectoryName(targetFile);
            if (!Directory.Exists(targetFileDir)) Directory.CreateDirectory(targetFileDir);

            bool shouldCopy = true;
            FileInfo sourceFileInfo = new FileInfo(file);

            if (job.Type == BackupType.Differential && File.Exists(targetFile))
            {
                if (sourceFileInfo.LastWriteTime <= new FileInfo(targetFile).LastWriteTime)
                {
                    shouldCopy = false;
                    int currentProgress = 0;

                    lock (_stateLock)
                    {
                        _stateTracker.UpdateState(job.Name, s =>
                        {
                            s.NbFilesLeftToDo--;
                            s.RemainingFilesSize -= sourceFileInfo.Length;
                            s.Progression = s.TotalFilesToCopy > 0 ? (int)((double)(s.TotalFilesToCopy - s.NbFilesLeftToDo) / s.TotalFilesToCopy * 100) : 0;
                            currentProgress = s.Progression;
                        });
                    }
                    OnJobProgress?.Invoke(job.Name, currentProgress);
                }
            }

            if (shouldCopy)
            {
                await CopyFileWithLoggingAsync(file, targetFile, job, sourceFileInfo.Length);
            }
        }

        private async Task CopyFileWithLoggingAsync(string source, string target, BackupJob job, long fileSize)
        {
            lock (_stateLock)
            {
                _stateTracker.UpdateState(job.Name, s =>
                {
                    s.SourceFilePath = source;
                    s.TargetFilePath = target;
                });
            }

            Stopwatch stopwatch = new Stopwatch();
            long timeMs = 0;

            var settings = _configManager.LoadSettings();
            bool shouldEncrypt = (settings.EncryptedExtensions ?? new List<string>())
                                 .Contains(Path.GetExtension(source).ToLower());

            long limitValue = settings.MaxParallelFileSizeLimit;
            long multiplier = settings.MaxParallelFileSizeLimitUnit switch
            {
                "Mo" => 1024L * 1024L,
                "Go" => 1024L * 1024L * 1024L,
                _ => 1024L // "Ko"
            };

            long limitInBytes = limitValue * multiplier;
            bool isLargeFile = fileSize > limitInBytes;
            bool isBlockedFired = false;

            try
            {
                if (isLargeFile)
                {
                    // Tente de récupérer le verrou immédiatement
                    bool acquired = await _largeFileLock.WaitAsync(0);
                    if (!acquired)
                    {
                        isBlockedFired = true;
                        OnJobBlocked?.Invoke(job.Name, true);
                        lock (_stateLock)
                        {
                            _stateTracker.UpdateState(job.Name, s => s.State = "BLOCKED");
                        }

                        // Patiente pour de vrai
                        await _largeFileLock.WaitAsync();

                        OnJobBlocked?.Invoke(job.Name, false);
                        lock (_stateLock)
                        {
                            _stateTracker.UpdateState(job.Name, s => s.State = "ACTIVE");
                        }
                    }
                }

                try
                {
                    stopwatch.Start();

                    if (shouldEncrypt) ExecuteCryptoSoft(source, target, job, settings.EncryptionKey);
                    else File.Copy(source, target, true);

                    stopwatch.Stop();
                    timeMs = stopwatch.ElapsedMilliseconds;

                    int currentProgress = 0;
                    lock (_stateLock)
                    {
                        _stateTracker.UpdateState(job.Name, s =>
                        {
                            s.NbFilesLeftToDo--;
                            s.RemainingFilesSize -= fileSize;
                            s.Progression = s.TotalFilesToCopy > 0 ? (int)((double)(s.TotalFilesToCopy - s.NbFilesLeftToDo) / s.TotalFilesToCopy * 100) : 0;
                            currentProgress = s.Progression;
                        });
                    }

                    OnJobProgress?.Invoke(job.Name, currentProgress);
                    OnProgressUpdate?.Invoke(source, 0);
                }
                catch (Exception)
                {
                    stopwatch.Stop();
                    timeMs = -1;
                }
            }
            finally
            {
                if (isLargeFile)
                {
                    _largeFileLock.Release();
                }
            }

            await DailyLogger.Instance.WriteLogAsync(new LogEntry
            {
                Name = job.Name,
                FileSource = source,
                FileTarget = target,
                FileSize = fileSize,
                FileTransferTime = timeMs
            }, job.Id.ToString(), settings.LogFormat);
        }

        private void ExecuteCryptoSoft(string source, string target, BackupJob job, string encryptionKey)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CryptoSoft.exe");
            if (!File.Exists(path)) throw new FileNotFoundException("CryptoSoft.exe missing.");

            bool isWaitingFired = false;

            bool acquired = _cryptoSoftLock.Wait(0);
            if (!acquired)
            {
                isWaitingFired = true;
                OnJobWaiting?.Invoke(job.Name, true);
                lock (_stateLock)
                {
                    _stateTracker.UpdateState(job.Name, s => s.State = "WAITING");
                }

                _cryptoSoftLock.Wait();

                OnJobWaiting?.Invoke(job.Name, false);
                lock (_stateLock)
                {
                    _stateTracker.UpdateState(job.Name, s => s.State = "ACTIVE");
                }
            }

            try
            {
                string safeKey = string.IsNullOrWhiteSpace(encryptionKey) ? "EasySaveKey" : encryptionKey;

                ProcessStartInfo psi = new ProcessStartInfo(path, $"\"{source}\" \"{target}\" \"{safeKey}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using Process p = Process.Start(psi);

                if (!p.WaitForExit(60000))
                {
                    p.Kill();
                    throw new TimeoutException("Timeout: CryptoSoft s'est bloqué et a dû être forcé à s'arrêter.");
                }

                if (p.ExitCode != 0)
                {
                    throw new InvalidOperationException($"CryptoSoft a échoué avec le code d'erreur : {p.ExitCode}");
                }
            }
            finally
            {
                _cryptoSoftLock.Release();
            }
        }
    }
}
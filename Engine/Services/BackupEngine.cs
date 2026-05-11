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
        public event Action<string> OnActivityMessage;

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
                // Nettoyage de la barrière globale en cas d'erreur ou d'annulation
                if (remainingPriorityFiles > 0) Interlocked.Add(ref GlobalPriorityFilesCount, -remainingPriorityFiles);

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
            bool shouldEncrypt = (settings.EncryptedExtensions ?? new List<string>()).Contains(Path.GetExtension(source).ToLower());

            long limitValue = settings.MaxParallelFileSizeLimit;
            long multiplier = settings.MaxParallelFileSizeLimitUnit switch
            {
                "Mo" => 1024L * 1024L,
                "Go" => 1024L * 1024L * 1024L,
                _ => 1024L // Par défaut "Ko"
            };

            long limitInBytes = limitValue * multiplier;
            bool isLargeFile = fileSize > limitInBytes;

            _jobPauseEvents.TryGetValue(job.Name, out var pauseEvent);
            _jobCancellationTokens.TryGetValue(job.Name, out var cts);
            var token = cts?.Token ?? CancellationToken.None;

            bool largeFileLockAcquired = false;

            try
            {
                if (isLargeFile)
                {
                    // Tente de prendre le verrou sans bloquer le thread (attente 0 ms)
                    if (await _largeFileLock.WaitAsync(0))
                    {
                        largeFileLockAcquired = true;
                    }
                    else
                    {
                        // Si le verrou est pris, on signale le blocage à l'UI
                        OnJobBlocked?.Invoke(job.Name, true);
                        lock (_stateLock) { _stateTracker.UpdateState(job.Name, s => s.State = "BLOCKED"); }

                        long excessBytes = fileSize - limitInBytes;
                        string excessStr = excessBytes >= 1024 * 1024 ? (excessBytes / (1024 * 1024)) + " Mo" : (excessBytes / 1024) + " Ko";
                        string msg = settings.Language == "FR"
                            ? $"[BLOQUÉ] {Path.GetFileName(source)} dépasse la limite de {excessStr}"
                            : $"[BLOCKED] {Path.GetFileName(source)} exceeds limit by {excessStr}";
                        OnActivityMessage?.Invoke(msg);

                        // Boucle d'attente qui reste sensible à la PAUSE et à l'ARRET (Stop)
                        while (true)
                        {
                            token.ThrowIfCancellationRequested();
                            pauseEvent?.WaitOne(); // Si le travail est mis en pause, le thread s'arrête ici sans "voler" le verrou
                            token.ThrowIfCancellationRequested();

                            if (await _largeFileLock.WaitAsync(200, token))
                            {
                                largeFileLockAcquired = true;
                                break;
                            }
                        }

                        OnJobBlocked?.Invoke(job.Name, false);
                        lock (_stateLock) { _stateTracker.UpdateState(job.Name, s => s.State = "ACTIVE"); }
                    }
                }

                try
                {
                    stopwatch.Start();

                    if (shouldEncrypt)
                        ExecuteCryptoSoft(source, target, job.Name, settings.EncryptionKey, pauseEvent, token);
                    else
                    {
                        token.ThrowIfCancellationRequested();
                        File.Copy(source, target, true);
                    }

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
                catch (OperationCanceledException)
                {
                    throw; // Remonter l'annulation pour interrompre le job entier proprement
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    timeMs = -1;
                    string errMsg = settings.Language == "FR" ? $"❌ Échec copie {Path.GetFileName(source)} : {ex.Message}" : $"❌ Copy failed {Path.GetFileName(source)} : {ex.Message}";
                    OnActivityMessage?.Invoke(errMsg);
                }
            }
            finally
            {
                // Ne libère le verrou que si le processus actuel a véritablement réussi à l'obtenir
                if (largeFileLockAcquired)
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

        private void ExecuteCryptoSoft(string source, string target, string jobName, string encryptionKey, ManualResetEvent pauseEvent, CancellationToken token)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CryptoSoft.exe");
            if (!File.Exists(path)) throw new FileNotFoundException("CryptoSoft.exe missing.");

            bool cryptoLockAcquired = false;

            try
            {
                if (_cryptoSoftLock.Wait(0))
                {
                    cryptoLockAcquired = true;
                }
                else
                {
                    OnJobWaiting?.Invoke(jobName, true);
                    lock (_stateLock) { _stateTracker.UpdateState(jobName, s => s.State = "WAITING"); }

                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        pauseEvent?.WaitOne();
                        token.ThrowIfCancellationRequested();

                        if (_cryptoSoftLock.Wait(200, token))
                        {
                            cryptoLockAcquired = true;
                            break;
                        }
                    }

                    OnJobWaiting?.Invoke(jobName, false);
                    lock (_stateLock) { _stateTracker.UpdateState(jobName, s => s.State = "ACTIVE"); }
                }

                string safeKey = string.IsNullOrWhiteSpace(encryptionKey) ? "EasySaveKey" : encryptionKey;
                ProcessStartInfo psi = new ProcessStartInfo(path, $"\"{source}\" \"{target}\" \"{safeKey}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using Process p = Process.Start(psi);
                int elapsed = 0;

                // Surveillance dynamique du processus pour permettre son annulation en plein vol
                while (!p.HasExited)
                {
                    if (token.IsCancellationRequested)
                    {
                        try { p.Kill(); } catch { }
                        token.ThrowIfCancellationRequested();
                    }

                    Thread.Sleep(100);
                    elapsed += 100;

                    if (elapsed > 60000)
                    {
                        try { p.Kill(); } catch { }
                        throw new TimeoutException("Timeout: CryptoSoft s'est bloqué et a dû être forcé à s'arrêter.");
                    }
                }

                if (p.ExitCode != 0)
                {
                    throw new InvalidOperationException($"CryptoSoft a échoué avec le code d'erreur : {p.ExitCode}");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            finally
            {
                if (cryptoLockAcquired)
                {
                    _cryptoSoftLock.Release();
                }
            }
        }
    }
}
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

        private readonly StateTracker _stateTracker;
        private readonly ConfigManager _configManager;
        private readonly object _stateLock = new object();

        // Your work: Global counter for priority files across all threads (Barrier concept)
        private static int GlobalPriorityFilesCount = 0;

        // Teammate's work: Thread control for Pause and Stop
        private readonly ConcurrentDictionary<string, ManualResetEvent> _jobPauseEvents = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _jobCancellationTokens = new();

        public BackupEngine(StateTracker stateTracker, ConfigManager configManager)
        {
            _stateTracker = stateTracker;
            _configManager = configManager;
        }

        // --- TEAMMATE'S FEATURE: PAUSE / RESUME / STOP ---

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
            ResumeJob(jobName); // Force resume to allow the thread to reach the cancellation token and die
        }

        // -------------------------------------------------

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

            // Initialize Thread Controls for this specific job
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

            Interlocked.Add(ref GlobalPriorityFilesCount, priorityFiles.Count);

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
                // Step 1: Priority files
                foreach (string file in priorityFiles)
                {
                    pauseEvent.WaitOne(); // Wait here if job is paused
                    cts.Token.ThrowIfCancellationRequested(); // Stop here if job is cancelled

                    await ProcessSingleFileAsync(file, job);
                    Interlocked.Decrement(ref GlobalPriorityFilesCount);
                }

                // Step 2: Normal files (Barrier)
                foreach (string file in normalFiles)
                {
                    // Global Barrier: Wait for all priority files across ALL threads
                    while (Interlocked.CompareExchange(ref GlobalPriorityFilesCount, 0, 0) > 0)
                    {
                        cts.Token.ThrowIfCancellationRequested(); // Allow cancellation even while waiting at the barrier
                        await Task.Delay(200);
                    }

                    pauseEvent.WaitOne();
                    cts.Token.ThrowIfCancellationRequested();

                    await ProcessSingleFileAsync(file, job);
                }
            }
            catch (OperationCanceledException)
            {
                // Job was manually stopped by user
                throw new Exception("Job stopped manually.");
            }
            finally
            {
                // Clean up memory and reset state
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

            try
            {
                stopwatch.Start();

                if (shouldEncrypt) ExecuteCryptoSoft(source, target);
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

            await DailyLogger.Instance.WriteLogAsync(new LogEntry
            {
                Name = job.Name,
                FileSource = source,
                FileTarget = target,
                FileSize = fileSize,
                FileTransferTime = timeMs
            }, job.Id.ToString(), settings.LogFormat);
        }

        private void ExecuteCryptoSoft(string source, string target)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CryptoSoft.exe");
            if (!File.Exists(path)) throw new FileNotFoundException("CryptoSoft.exe missing.");

            ProcessStartInfo psi = new ProcessStartInfo(path, $"\"{source}\" \"{target}\"")
            { CreateNoWindow = true, UseShellExecute = false };

            using Process p = Process.Start(psi);
            p.WaitForExit();
        }
    }
}
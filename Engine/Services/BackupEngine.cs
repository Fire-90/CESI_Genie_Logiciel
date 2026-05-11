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
        public event Action<string, bool, string> OnJobSuspendedBySoftware;
        public event Action<string> OnActivityMessage;

        private readonly StateTracker _stateTracker;
        private readonly ConfigManager _configManager;
        private readonly object _stateLock = new object();

        private static int GlobalPriorityFilesCount = 0;
        private static int PausedPriorityFilesCount = 0;
        private readonly ConcurrentDictionary<string, int> _priorityFilesRemainingPerJob = new();

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
            if (_jobPauseEvents.TryGetValue(jobName, out var pauseEvent))
            {
                if (pauseEvent.WaitOne(0))
                {
                    pauseEvent.Reset();
                    if (_priorityFilesRemainingPerJob.TryGetValue(jobName, out int remaining))
                        Interlocked.Add(ref PausedPriorityFilesCount, remaining);
                }
            }
        }

        public void ResumeJob(string jobName)
        {
            if (_jobPauseEvents.TryGetValue(jobName, out var pauseEvent))
            {
                if (!pauseEvent.WaitOne(0))
                {
                    if (_priorityFilesRemainingPerJob.TryGetValue(jobName, out int remaining))
                        Interlocked.Add(ref PausedPriorityFilesCount, -remaining);
                    pauseEvent.Set();
                }
            }
        }

        public void StopJob(string jobName)
        {
            if (_jobCancellationTokens.TryGetValue(jobName, out var cts))
            {
                cts.Cancel();
                ResumeJob(jobName);
            }
        }

        public string GetRunningBusinessSoftware()
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

        private async Task CheckAndWaitForBusinessSoftwareAsync(string jobName, CancellationToken token)
        {
            bool wasSuspendedBySoftware = false;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                string blocking = GetRunningBusinessSoftware();
                if (blocking == null) break;

                if (!wasSuspendedBySoftware)
                {
                    wasSuspendedBySoftware = true;
                    OnJobSuspendedBySoftware?.Invoke(jobName, true, blocking);
                    lock (_stateLock) { _stateTracker.UpdateState(jobName, s => s.State = "SUSPENDED"); }
                }
                await Task.Delay(1000, token);
            }

            if (wasSuspendedBySoftware)
            {
                OnJobSuspendedBySoftware?.Invoke(jobName, false, null);
                lock (_stateLock) { _stateTracker.UpdateState(jobName, s => s.State = "ACTIVE"); }
            }
        }

        public async Task ExecuteJobAsync(BackupJob job)
        {
            if (!Directory.Exists(job.SourceDirectory)) throw new DirectoryNotFoundException("Source directory not found.");

            var cts = new CancellationTokenSource();
            var pauseEvent = new ManualResetEvent(true);
            _jobCancellationTokens[job.Name] = cts;
            _jobPauseEvents[job.Name] = pauseEvent;

            string[] allFiles = Directory.GetFiles(job.SourceDirectory, "*.*", SearchOption.AllDirectories);
            var settings = _configManager.LoadSettings();
            List<string> priorityExtensions = settings.PriorityExtensions ?? new List<string>();

            var priorityFiles = allFiles.Where(f => priorityExtensions.Contains(Path.GetExtension(f).ToLower())).ToList();
            var normalFiles = allFiles.Where(f => !priorityExtensions.Contains(Path.GetExtension(f).ToLower())).ToList();

            int totalFilesToCopy = allFiles.Length;
            int remainingPriorityFiles = priorityFiles.Count;

            _priorityFilesRemainingPerJob[job.Name] = remainingPriorityFiles;
            Interlocked.Add(ref GlobalPriorityFilesCount, remainingPriorityFiles);

            lock (_stateLock) { _stateTracker.UpdateState(job.Name, s => { s.State = "ACTIVE"; s.TotalFilesToCopy = totalFilesToCopy; s.NbFilesLeftToDo = totalFilesToCopy; s.Progression = 0; }); }

            try
            {
                // SIGNAL DE DÉBUT
                OnActivityMessage?.Invoke($"{job.Name} : START");

                foreach (string file in priorityFiles)
                {
                    await CheckAndWaitForBusinessSoftwareAsync(job.Name, cts.Token);
                    pauseEvent.WaitOne();
                    cts.Token.ThrowIfCancellationRequested();

                    try { await ProcessSingleFileAsync(file, job, cts.Token); }
                    finally
                    {
                        Interlocked.Decrement(ref GlobalPriorityFilesCount);
                        _priorityFilesRemainingPerJob.AddOrUpdate(job.Name, 0, (k, v) => v - 1);
                        if (!pauseEvent.WaitOne(0)) Interlocked.Decrement(ref PausedPriorityFilesCount);
                        remainingPriorityFiles--;
                    }
                }

                while (true)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    if ((GlobalPriorityFilesCount - PausedPriorityFilesCount) <= 0) break;
                    await Task.Delay(200, cts.Token);
                }

                foreach (string file in normalFiles)
                {
                    await CheckAndWaitForBusinessSoftwareAsync(job.Name, cts.Token);
                    pauseEvent.WaitOne();
                    cts.Token.ThrowIfCancellationRequested();
                    await ProcessSingleFileAsync(file, job, cts.Token);
                }
            }
            catch (OperationCanceledException) { throw new Exception("Job stopped manually."); }
            finally
            {
                // SIGNAL DE FIN
                OnActivityMessage?.Invoke($"{job.Name} : END");

                if (remainingPriorityFiles > 0)
                {
                    Interlocked.Add(ref GlobalPriorityFilesCount, -remainingPriorityFiles);
                    if (!pauseEvent.WaitOne(0)) Interlocked.Add(ref PausedPriorityFilesCount, -remainingPriorityFiles);
                }
                _priorityFilesRemainingPerJob.TryRemove(job.Name, out _);
                _jobCancellationTokens.TryRemove(job.Name, out _);
                _jobPauseEvents.TryRemove(job.Name, out _);

                lock (_stateLock) { _stateTracker.UpdateState(job.Name, s => { s.State = "END"; s.Progression = 0; }); }
                OnJobProgress?.Invoke(job.Name, 100);
            }
        }

        private async Task ProcessSingleFileAsync(string file, BackupJob job, CancellationToken token)
        {
            string blocking = GetRunningBusinessSoftware();
            if (blocking != null) await CheckAndWaitForBusinessSoftwareAsync(job.Name, token);

            string relativePath = Path.GetRelativePath(job.SourceDirectory, file);
            string targetFile = Path.Combine(job.TargetDirectory, relativePath);
            string targetFileDir = Path.GetDirectoryName(targetFile);
            if (!Directory.Exists(targetFileDir)) Directory.CreateDirectory(targetFileDir);

            FileInfo sourceFileInfo = new FileInfo(file);
            bool shouldCopy = true;

            if (job.Type == BackupType.Differential && File.Exists(targetFile))
            {
                if (sourceFileInfo.LastWriteTime <= new FileInfo(targetFile).LastWriteTime)
                {
                    shouldCopy = false;
                    lock (_stateLock) { _stateTracker.UpdateState(job.Name, s => { s.NbFilesLeftToDo--; s.Progression = s.TotalFilesToCopy > 0 ? (int)((double)(s.TotalFilesToCopy - s.NbFilesLeftToDo) / s.TotalFilesToCopy * 100) : 0; OnJobProgress?.Invoke(job.Name, s.Progression); }); }
                }
            }

            if (shouldCopy) await CopyFileWithLoggingAsync(file, targetFile, job, sourceFileInfo.Length, token);
        }

        private async Task CopyFileWithLoggingAsync(string source, string target, BackupJob job, long fileSize, CancellationToken token)
        {
            lock (_stateLock) { _stateTracker.UpdateState(job.Name, s => { s.SourceFilePath = source; s.TargetFilePath = target; }); }
            Stopwatch stopwatch = new Stopwatch();
            long timeMs = 0;
            var settings = _configManager.LoadSettings();
            bool shouldEncrypt = (settings.EncryptedExtensions ?? new List<string>()).Contains(Path.GetExtension(source).ToLower());
            long limitInBytes = settings.MaxParallelFileSizeLimit * (settings.MaxParallelFileSizeLimitUnit switch { "Mo" => 1024L * 1024L, "Go" => 1024L * 1024L * 1024L, _ => 1024L });
            bool isLargeFile = fileSize > limitInBytes;
            bool largeFileLockAcquired = false;

            try
            {
                if (isLargeFile)
                {
                    if (await _largeFileLock.WaitAsync(0)) { largeFileLockAcquired = true; }
                    else
                    {
                        OnJobBlocked?.Invoke(job.Name, true);
                        lock (_stateLock) { _stateTracker.UpdateState(job.Name, s => s.State = "BLOCKED"); }
                        while (true)
                        {
                            token.ThrowIfCancellationRequested();
                            await CheckAndWaitForBusinessSoftwareAsync(job.Name, token);
                            if (_jobPauseEvents.TryGetValue(job.Name, out var ev)) ev.WaitOne();
                            if (await _largeFileLock.WaitAsync(200, token)) { largeFileLockAcquired = true; break; }
                        }
                        OnJobBlocked?.Invoke(job.Name, false);
                        lock (_stateLock) { _stateTracker.UpdateState(job.Name, s => s.State = "ACTIVE"); }
                    }
                }

                stopwatch.Start();
                if (shouldEncrypt) ExecuteCryptoSoft(source, target, job.Name, settings.EncryptionKey, token);
                else { token.ThrowIfCancellationRequested(); File.Copy(source, target, true); }
                stopwatch.Stop();
                timeMs = stopwatch.ElapsedMilliseconds;

                lock (_stateLock) { _stateTracker.UpdateState(job.Name, s => { s.NbFilesLeftToDo--; s.Progression = s.TotalFilesToCopy > 0 ? (int)((double)(s.TotalFilesToCopy - s.NbFilesLeftToDo) / s.TotalFilesToCopy * 100) : 0; OnJobProgress?.Invoke(job.Name, s.Progression); }); }
                OnProgressUpdate?.Invoke(source, 0);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { stopwatch.Stop(); timeMs = -1; OnActivityMessage?.Invoke($"❌ Error: {ex.Message}"); }
            finally { if (largeFileLockAcquired) _largeFileLock.Release(); }

            await DailyLogger.Instance.WriteLogAsync(new LogEntry { Name = job.Name, FileSource = source, FileTarget = target, FileSize = fileSize, FileTransferTime = timeMs }, job.Id.ToString(), settings.LogFormat);
        }

        private void ExecuteCryptoSoft(string source, string target, string jobName, string encryptionKey, CancellationToken token)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CryptoSoft.exe");
            if (!File.Exists(path)) throw new FileNotFoundException("CryptoSoft.exe missing.");

            bool cryptoLockAcquired = false;
            try
            {
                if (_cryptoSoftLock.Wait(0)) { cryptoLockAcquired = true; }
                else
                {
                    OnJobWaiting?.Invoke(jobName, true);
                    lock (_stateLock) { _stateTracker.UpdateState(jobName, s => s.State = "WAITING"); }
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        if (_cryptoSoftLock.Wait(200, token)) { cryptoLockAcquired = true; break; }
                    }
                    OnJobWaiting?.Invoke(jobName, false);
                    lock (_stateLock) { _stateTracker.UpdateState(jobName, s => s.State = "ACTIVE"); }
                }

                string safeKey = string.IsNullOrWhiteSpace(encryptionKey) ? "EasySaveKey" : encryptionKey;
                ProcessStartInfo psi = new ProcessStartInfo(path, $"\"{source}\" \"{target}\" \"{safeKey}\"") { CreateNoWindow = true, UseShellExecute = false };
                using Process p = Process.Start(psi);
                while (!p.HasExited)
                {
                    if (token.IsCancellationRequested) { try { p.Kill(); } catch { } token.ThrowIfCancellationRequested(); }
                    Thread.Sleep(100);
                }
                if (p.ExitCode != 0) throw new InvalidOperationException($"CryptoSoft Error: {p.ExitCode}");
            }
            finally { if (cryptoLockAcquired) _cryptoSoftLock.Release(); }
        }
    }
}
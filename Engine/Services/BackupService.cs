using EasyLog;
using EasySave.Models;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace EasySave.Services
{
    public class BackupService
    {
        public delegate void ProgressUpdateHandler(string currentFile, int remainingFiles);

        public event ProgressUpdateHandler OnProgressUpdate;
        public event Action<string, int, string> OnJobProgress;
        public event Action<string, bool> OnJobWaiting;
        public event Action<string, bool> OnJobBlocked;
        public event Action<string, bool, string> OnJobSuspendedBySoftware;
        public event Action<string> OnActivityMessage;

        public event Action<string> OnJobActuallyPaused;

        private readonly StateService _stateTracker;
        private readonly SettingService _configManager;
        private readonly object _stateLock = new object();

        private static int GlobalPriorityFilesCount = 0;
        private static int PausedPriorityFilesCount = 0;
        private readonly ConcurrentDictionary<string, int> _priorityFilesRemainingPerJob = new();

        private static readonly SemaphoreSlim _cryptoSoftLock = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim _largeFileLock = new SemaphoreSlim(1, 1);

        private readonly ConcurrentDictionary<string, ManualResetEvent> _jobPauseEvents = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _jobCancellationTokens = new();

        private readonly ConcurrentDictionary<string, Stopwatch> _jobSpeedStopwatches = new();
        private readonly ConcurrentDictionary<string, long> _jobBytesSinceLastUpdate = new();

        public BackupService(StateService stateTracker, SettingService configManager)
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

                    var settings = _configManager.LoadSettings();
                    if (settings.PauseBehavior != "AfterFile")
                    {
                        lock (_stateLock)
                        {
                            _stateTracker.UpdateState(jobName, s =>
                            {
                                s.State = "SUSPENDED";
                                s.CurrentSpeed = "";
                                OnJobProgress?.Invoke(jobName, s.Progression, s.CurrentSpeed);
                            });
                        }
                    }
                    else
                    {
                        lock (_stateLock)
                        {
                            _stateTracker.UpdateState(jobName, s =>
                            {
                                s.State = "PAUSE_PENDING";
                            });
                        }
                        OnActivityMessage?.Invoke($"{jobName} : [PAUSE_PENDING]");
                    }
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
                    lock (_stateLock) { _stateTracker.UpdateState(jobName, s => s.State = "ACTIVE"); }
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

                    lock (_stateLock)
                    {
                        _stateTracker.UpdateState(jobName, s =>
                        {
                            s.State = "SUSPENDED";
                            s.CurrentSpeed = "";
                            OnJobProgress?.Invoke(jobName, s.Progression, s.CurrentSpeed);
                        });
                    }
                    OnActivityMessage?.Invoke($"{jobName} : [SOFTWARE_PAUSE] {blocking}");
                }
                await Task.Delay(1000, token);
            }

            if (wasSuspendedBySoftware)
            {
                OnJobSuspendedBySoftware?.Invoke(jobName, false, null);
                lock (_stateLock) { _stateTracker.UpdateState(jobName, s => s.State = "ACTIVE"); }
                OnActivityMessage?.Invoke($"{jobName} : [RESUME]");
            }
        }

        public async Task ExecuteJobAsync(BackupJob job)
        {
            if (!Directory.Exists(job.SourceDirectory)) throw new DirectoryNotFoundException("Source directory not found.");

            var cts = new CancellationTokenSource();
            var pauseEvent = new ManualResetEvent(true);
            _jobCancellationTokens[job.Name] = cts;
            _jobPauseEvents[job.Name] = pauseEvent;

            _jobSpeedStopwatches[job.Name] = Stopwatch.StartNew();
            _jobBytesSinceLastUpdate[job.Name] = 0;

            string[] allFiles = Directory.GetFiles(job.SourceDirectory, "*.*", SearchOption.AllDirectories);
            var settings = _configManager.LoadSettings();
            List<string> priorityExtensions = settings.PriorityExtensions ?? new List<string>();

            var priorityFiles = allFiles.Where(f => priorityExtensions.Contains(Path.GetExtension(f).ToLower())).ToList();
            var normalFiles = allFiles.Where(f => !priorityExtensions.Contains(Path.GetExtension(f).ToLower())).ToList();

            int totalFilesToCopy = allFiles.Length;
            int remainingPriorityFiles = priorityFiles.Count;

            _priorityFilesRemainingPerJob[job.Name] = remainingPriorityFiles;
            Interlocked.Add(ref GlobalPriorityFilesCount, remainingPriorityFiles);

            lock (_stateLock) { _stateTracker.UpdateState(job.Name, s => { s.State = "ACTIVE"; s.TotalFilesToCopy = totalFilesToCopy; s.NbFilesLeftToDo = totalFilesToCopy; s.Progression = 0; s.CurrentSpeed = ""; }); }

            try
            {
                OnActivityMessage?.Invoke($"{job.Name} : [START]");

                foreach (string file in priorityFiles)
                {
                    await CheckAndWaitForBusinessSoftwareAsync(job.Name, cts.Token);

                    if (_jobPauseEvents.TryGetValue(job.Name, out var pe) && !pe.WaitOne(0))
                    {
                        lock (_stateLock)
                        {
                            _stateTracker.UpdateState(job.Name, s =>
                            {
                                s.State = "SUSPENDED";
                                s.CurrentSpeed = "";
                                OnJobProgress?.Invoke(job.Name, s.Progression, s.CurrentSpeed);
                            });
                        }
                        OnJobActuallyPaused?.Invoke(job.Name);
                        pe.WaitOne();
                        lock (_stateLock) { _stateTracker.UpdateState(job.Name, s => s.State = "ACTIVE"); }
                    }
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

                    if (_jobPauseEvents.TryGetValue(job.Name, out var pe) && !pe.WaitOne(0))
                    {
                        lock (_stateLock)
                        {
                            _stateTracker.UpdateState(job.Name, s =>
                            {
                                s.State = "SUSPENDED";
                                s.CurrentSpeed = "";
                                OnJobProgress?.Invoke(job.Name, s.Progression, s.CurrentSpeed);
                            });
                        }
                        OnJobActuallyPaused?.Invoke(job.Name);
                        pe.WaitOne();
                        lock (_stateLock) { _stateTracker.UpdateState(job.Name, s => s.State = "ACTIVE"); }
                    }
                    cts.Token.ThrowIfCancellationRequested();

                    await ProcessSingleFileAsync(file, job, cts.Token);
                }
            }
            catch (OperationCanceledException) { throw new Exception("Job stopped manually."); }
            finally
            {
                OnActivityMessage?.Invoke($"{job.Name} : [END]");

                if (remainingPriorityFiles > 0)
                {
                    Interlocked.Add(ref GlobalPriorityFilesCount, -remainingPriorityFiles);
                    if (!pauseEvent.WaitOne(0)) Interlocked.Add(ref PausedPriorityFilesCount, -remainingPriorityFiles);
                }

                _priorityFilesRemainingPerJob.TryRemove(job.Name, out _);
                _jobCancellationTokens.TryRemove(job.Name, out _);
                _jobPauseEvents.TryRemove(job.Name, out _);
                _jobSpeedStopwatches.TryRemove(job.Name, out _);
                _jobBytesSinceLastUpdate.TryRemove(job.Name, out _);

                lock (_stateLock) { _stateTracker.UpdateState(job.Name, s => { s.State = "FINISHED"; s.Progression = 100; s.CurrentSpeed = ""; }); }
                OnJobProgress?.Invoke(job.Name, 100, "");

                Task.Run(async () =>
                {
                    await Task.Delay(2000);
                    lock (_stateLock)
                    {
                        _stateTracker.UpdateState(job.Name, s =>
                        {
                            s.State = "INACTIVE";
                            s.Progression = 0;
                            s.NbFilesLeftToDo = 0;
                            s.CurrentSpeed = "";
                        });
                    }
                });
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
                    lock (_stateLock) { _stateTracker.UpdateState(job.Name, s => { s.NbFilesLeftToDo--; s.Progression = s.TotalFilesToCopy > 0 ? (int)((double)(s.TotalFilesToCopy - s.NbFilesLeftToDo) / s.TotalFilesToCopy * 100) : 0; OnJobProgress?.Invoke(job.Name, s.Progression, s.CurrentSpeed); }); }
                }
            }

            if (shouldCopy) await CopyFileWithLoggingAsync(file, targetFile, job, sourceFileInfo.Length, token);
        }

        private async Task CopyFileWithLoggingAsync(string source, string target, BackupJob job, long fileSize, CancellationToken token)
        {
            lock (_stateLock) { _stateTracker.UpdateState(job.Name, s => { s.SourceFilePath = source; s.TargetFilePath = target; }); }

            Stopwatch totalSw = new Stopwatch();
            long encryptionTime = 0;

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
                        lock (_stateLock)
                        {
                            _stateTracker.UpdateState(job.Name, s =>
                            {
                                s.State = "BLOCKED";
                                s.CurrentSpeed = "";
                                OnJobProgress?.Invoke(job.Name, s.Progression, s.CurrentSpeed);
                            });
                        }
                        OnActivityMessage?.Invoke($"{job.Name} : [BLOCKED_SIZE] {Path.GetFileName(source)}");

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

                totalSw.Start();
                if (shouldEncrypt)
                {
                    encryptionTime = await ExecuteCryptoSoftAsync(source, target, job.Name, settings.EncryptionKey, token);
                }
                else
                {
                    await CopyFileAsync(source, target, job, fileSize, token, isLargeFile, val => largeFileLockAcquired = val);
                }
                totalSw.Stop();

                lock (_stateLock)
                {
                    _stateTracker.UpdateState(job.Name, s =>
                    {
                        s.NbFilesLeftToDo--;
                        s.Progression = s.TotalFilesToCopy > 0 ? (int)((double)(s.TotalFilesToCopy - s.NbFilesLeftToDo) / s.TotalFilesToCopy * 100) : 0;
                        OnJobProgress?.Invoke(job.Name, s.Progression, s.CurrentSpeed);
                    });
                }
                OnProgressUpdate?.Invoke(source, 0);

                await DailyLogger.Instance.WriteLogAsync(new LogEntry
                {
                    Name = job.Name,
                    FileSource = source,
                    FileTarget = target,
                    FileSize = fileSize,
                    FileTransferTime = totalSw.ElapsedMilliseconds,
                    EncryptionTime = encryptionTime
                }, job.Id.ToString(), settings.LogFormat);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                totalSw.Stop();
                OnActivityMessage?.Invoke($"{job.Name} : [ERROR] {ex.Message}");
                await DailyLogger.Instance.WriteLogAsync(new LogEntry
                {
                    Name = job.Name,
                    FileSource = source,
                    FileTarget = target,
                    FileSize = fileSize,
                    FileTransferTime = -1,
                    EncryptionTime = -1
                }, job.Id.ToString(), settings.LogFormat);
            }
            finally { if (largeFileLockAcquired) _largeFileLock.Release(); }
        }

        private async Task CopyFileAsync(string source, string target, BackupJob job, long fileSize, CancellationToken token, bool isLargeFile, Action<bool> updateLockStatus)
        {
            byte[] buffer = new byte[1024 * 1024];
            bool pauseAfterFile = _configManager.LoadSettings().PauseBehavior == "AfterFile";

            using (FileStream sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream targetStream = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                long totalRead = 0;
                int currentRead;

                while ((currentRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                {
                    bool wasPaused = false;

                    bool hasBusinessSoftware = GetRunningBusinessSoftware() != null;
                    bool isManuallyPaused = _jobPauseEvents.TryGetValue(job.Name, out var pe) && !pe.WaitOne(0);

                    if (hasBusinessSoftware || (isManuallyPaused && !pauseAfterFile))
                    {
                        wasPaused = true;

                        if (isLargeFile)
                        {
                            _largeFileLock.Release();
                            updateLockStatus(false);
                        }

                        if (hasBusinessSoftware) await CheckAndWaitForBusinessSoftwareAsync(job.Name, token);
                        if (!pauseAfterFile && _jobPauseEvents.TryGetValue(job.Name, out var evWait)) evWait.WaitOne();
                        token.ThrowIfCancellationRequested();

                        if (isLargeFile)
                        {
                            bool blockedEventFired = false;
                            while (true)
                            {
                                token.ThrowIfCancellationRequested();

                                bool stillNeedsWait = GetRunningBusinessSoftware() != null || (!pauseAfterFile && _jobPauseEvents.TryGetValue(job.Name, out var pe2) && !pe2.WaitOne(0));
                                if (stillNeedsWait)
                                {
                                    if (blockedEventFired) { OnJobBlocked?.Invoke(job.Name, false); blockedEventFired = false; }
                                    await CheckAndWaitForBusinessSoftwareAsync(job.Name, token);
                                    if (!pauseAfterFile && _jobPauseEvents.TryGetValue(job.Name, out var evWait2)) evWait2.WaitOne();
                                    continue;
                                }

                                if (await _largeFileLock.WaitAsync(200, token))
                                {
                                    updateLockStatus(true);
                                    if (blockedEventFired)
                                    {
                                        OnJobBlocked?.Invoke(job.Name, false);
                                        lock (_stateLock) { _stateTracker.UpdateState(job.Name, s => s.State = "ACTIVE"); }
                                    }
                                    break;
                                }
                                else if (!blockedEventFired)
                                {
                                    blockedEventFired = true;
                                    OnJobBlocked?.Invoke(job.Name, true);
                                    lock (_stateLock)
                                    {
                                        _stateTracker.UpdateState(job.Name, s =>
                                        {
                                            s.State = "BLOCKED";
                                            s.CurrentSpeed = "";
                                            OnJobProgress?.Invoke(job.Name, s.Progression, s.CurrentSpeed);
                                        });
                                    }
                                }
                            }
                        }
                    }

                    await targetStream.WriteAsync(buffer, 0, currentRead, token);
                    totalRead += currentRead;

                    bool isJobSw = _jobSpeedStopwatches.TryGetValue(job.Name, out var jobSw);
                    if (isJobSw)
                    {
                        if (wasPaused)
                        {
                            jobSw.Restart();
                            _jobBytesSinceLastUpdate[job.Name] = 0;
                        }
                        else
                        {
                            _jobBytesSinceLastUpdate.AddOrUpdate(job.Name, currentRead, (k, v) => v + currentRead);
                        }
                    }

                    bool forceUpdate = totalRead == fileSize;
                    bool timeElapsed = isJobSw && jobSw.ElapsedMilliseconds >= 500;

                    if (timeElapsed || forceUpdate)
                    {
                        string speedStr = null;

                        if (timeElapsed)
                        {
                            double seconds = jobSw.Elapsed.TotalSeconds;
                            long bytesCopied = _jobBytesSinceLastUpdate[job.Name];

                            if (seconds > 0)
                            {
                                double mbPerSec = (bytesCopied / seconds) / (1024.0 * 1024.0);
                                speedStr = $"{mbPerSec:F1} Mo/s";
                            }

                            jobSw.Restart();
                            _jobBytesSinceLastUpdate[job.Name] = 0;
                        }

                        lock (_stateLock)
                        {
                            _stateTracker.UpdateState(job.Name, s =>
                            {
                                double fileProgress = fileSize > 0 ? (double)totalRead / fileSize : 0;
                                int overallProgress = s.TotalFilesToCopy > 0
                                    ? (int)((((s.TotalFilesToCopy - s.NbFilesLeftToDo) + fileProgress) / s.TotalFilesToCopy) * 100)
                                    : 0;

                                s.Progression = overallProgress;
                                if (speedStr != null) s.CurrentSpeed = speedStr;

                                OnJobProgress?.Invoke(job.Name, s.Progression, s.CurrentSpeed);
                            });
                        }
                    }
                }
            }
        }

        private async Task<long> ExecuteCryptoSoftAsync(string source, string target, string jobName, string encryptionKey, CancellationToken token)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CryptoSoft.exe");
            if (!File.Exists(path)) throw new FileNotFoundException("CryptoSoft.exe missing.");

            bool cryptoLockAcquired = false;
            try
            {
                if (await _cryptoSoftLock.WaitAsync(0)) { cryptoLockAcquired = true; }
                else
                {
                    OnJobWaiting?.Invoke(jobName, true);
                    lock (_stateLock) { _stateTracker.UpdateState(jobName, s => s.State = "WAITING"); }
                    OnActivityMessage?.Invoke($"{jobName} : [WAITING_CRYPTO]");

                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        if (await _cryptoSoftLock.WaitAsync(200, token)) { cryptoLockAcquired = true; break; }
                    }
                    OnJobWaiting?.Invoke(jobName, false);
                    lock (_stateLock) { _stateTracker.UpdateState(jobName, s => s.State = "ACTIVE"); }
                }

                lock (_stateLock)
                {
                    _stateTracker.UpdateState(jobName, s =>
                    {
                        s.CurrentSpeed = "ENCRYPTING";
                        OnJobProgress?.Invoke(jobName, s.Progression, s.CurrentSpeed);
                    });
                }

                string safeKey = string.IsNullOrWhiteSpace(encryptionKey) ? "EasySaveKey" : encryptionKey;
                ProcessStartInfo psi = new ProcessStartInfo(path, $"\"{source}\" \"{target}\" \"{safeKey}\"") { CreateNoWindow = true, UseShellExecute = false };

                Stopwatch cryptoSw = new Stopwatch();
                using Process p = Process.Start(psi);
                cryptoSw.Start();

                while (!p.HasExited)
                {
                    if (token.IsCancellationRequested) { try { p.Kill(); } catch { } token.ThrowIfCancellationRequested(); }
                    await Task.Delay(100, token);
                }
                cryptoSw.Stop();

                if (p.ExitCode != 0) return -p.ExitCode;
                return cryptoSw.ElapsedMilliseconds;
            }
            finally { if (cryptoLockAcquired) _cryptoSoftLock.Release(); }
        }
    }
}
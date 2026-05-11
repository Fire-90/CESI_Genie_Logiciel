using System;
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

        private StateTracker _stateTracker;
        private readonly ConfigManager _configManager;
        private readonly object _stateLock = new object();

        // GLOBAL COUNTER: Shared across ALL running BackupEngine instances (The "Barrier" concept)
        private static int GlobalPriorityFilesCount = 0;

        public BackupEngine(StateTracker stateTracker, ConfigManager configManager)
        {
            _stateTracker = stateTracker;
            _configManager = configManager;
        }

        private string GetRunningBusinessSoftware()
        {
            var settings = _configManager.LoadSettings();
            var businessSoftwares = settings.BusinessSoftwares;

            if (businessSoftwares == null) return null;

            foreach (string software in businessSoftwares)
            {
                if (string.IsNullOrWhiteSpace(software)) continue;

                Process[] processes = Process.GetProcessesByName(software);
                if (processes.Length > 0) return software;
            }
            return null;
        }

        public async Task ExecuteJobAsync(BackupJob job)
        {
            string blockingSoftware = GetRunningBusinessSoftware();
            if (blockingSoftware != null)
            {
                throw new Exception($"Lancement impossible : Le logiciel métier '{blockingSoftware}' est ouvert.");
            }

            if (string.IsNullOrWhiteSpace(job.SourceDirectory) || !Directory.Exists(job.SourceDirectory))
            {
                throw new DirectoryNotFoundException($"Source invalide ou introuvable pour {job.Name}");
            }

            // 1. Gather all files and Settings
            string[] allFiles = Directory.GetFiles(job.SourceDirectory, "*.*", SearchOption.AllDirectories);
            var settings = _configManager.LoadSettings();
            List<string> priorityExtensions = settings.PriorityExtensions ?? new List<string>();

            // 2. Sort files into Priority and Normal lists
            var priorityFiles = new List<string>();
            var normalFiles = new List<string>();

            foreach (string file in allFiles)
            {
                string extension = Path.GetExtension(file).ToLower();
                if (priorityExtensions.Contains(extension))
                {
                    priorityFiles.Add(file);
                }
                else
                {
                    normalFiles.Add(file);
                }
            }

            int totalFilesToCopy = priorityFiles.Count + normalFiles.Count;
            long totalFilesSize = allFiles.Sum(f => new FileInfo(f).Length);

            // 3. Register priority files in the global barrier counter safely
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
                // 4. STEP ONE: Process PRIORITY files first
                foreach (string file in priorityFiles)
                {
                    await ProcessSingleFileAsync(file, job);

                    // Decrease global priority counter safely when a priority file is done
                    Interlocked.Decrement(ref GlobalPriorityFilesCount);
                }

                // 5. STEP TWO: Process NORMAL files (The Barrier)
                foreach (string file in normalFiles)
                {
                    // Barrier: Wait until ALL running jobs have finished their priority files
                    while (Interlocked.CompareExchange(ref GlobalPriorityFilesCount, 0, 0) > 0)
                    {
                        // Sleep briefly to prevent high CPU usage while waiting
                        await Task.Delay(200);
                    }

                    await ProcessSingleFileAsync(file, job);
                }
            }
            finally
            {
                // Failsafe: Ensure state is always reset to END even if an exception occurs
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

        // Extracted file processing logic to avoid code duplication
        private async Task ProcessSingleFileAsync(string file, BackupJob job)
        {
            string currentBlockingSoftware = GetRunningBusinessSoftware();
            if (currentBlockingSoftware != null)
            {
                throw new Exception($"Sauvegarde interrompue : Détection du logiciel '{currentBlockingSoftware}'.");
            }

            string relativePath = Path.GetRelativePath(job.SourceDirectory, file);
            string targetFile = Path.Combine(job.TargetDirectory, relativePath);

            string targetFileDir = Path.GetDirectoryName(targetFile);
            if (!Directory.Exists(targetFileDir))
            {
                Directory.CreateDirectory(targetFileDir);
            }

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
                await CopyFileWithLoggingAsync(file, targetFile, job.Name, sourceFileInfo.Length);
            }
        }

        private async Task CopyFileWithLoggingAsync(string source, string target, string jobName, long fileSize)
        {
            lock (_stateLock)
            {
                _stateTracker.UpdateState(jobName, s =>
                {
                    s.SourceFilePath = source;
                    s.TargetFilePath = target;
                });
            }

            Stopwatch stopwatch = new Stopwatch();
            long timeMs = 0;

            var settings = _configManager.LoadSettings();
            List<string> encryptedExtensions = settings.EncryptedExtensions ?? new List<string>();
            string fileExtension = Path.GetExtension(source).ToLower();
            bool shouldEncrypt = encryptedExtensions.Contains(fileExtension);

            try
            {
                stopwatch.Start();

                if (shouldEncrypt)
                {
                    string cryptoSoftPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CryptoSoft.exe");

                    if (File.Exists(cryptoSoftPath))
                    {
                        ProcessStartInfo startInfo = new ProcessStartInfo
                        {
                            FileName = cryptoSoftPath,
                            Arguments = $"\"{source}\" \"{target}\"",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };

                        using (Process process = Process.Start(startInfo))
                        {
                            process.WaitForExit();
                            if (process.ExitCode != 0) throw new Exception("Erreur d'exécution de CryptoSoft.");
                        }
                    }
                    else
                    {
                        throw new FileNotFoundException("Exécutable CryptoSoft.exe introuvable.");
                    }
                }
                else
                {
                    File.Copy(source, target, true);
                }

                stopwatch.Stop();
                timeMs = stopwatch.ElapsedMilliseconds;

                int currentProgress = 0;
                lock (_stateLock)
                {
                    _stateTracker.UpdateState(jobName, s =>
                    {
                        s.NbFilesLeftToDo--;
                        s.RemainingFilesSize -= fileSize;
                        s.Progression = s.TotalFilesToCopy > 0 ? (int)((double)(s.TotalFilesToCopy - s.NbFilesLeftToDo) / s.TotalFilesToCopy * 100) : 0;
                        currentProgress = s.Progression;
                    });
                }

                OnJobProgress?.Invoke(jobName, currentProgress);
                OnProgressUpdate?.Invoke(source, 0);
            }
            catch (Exception)
            {
                stopwatch.Stop();
                timeMs = -1;
            }

            await DailyLogger.Instance.WriteLogAsync(new LogEntry
            {
                Name = jobName,
                FileSource = source,
                FileTarget = target,
                FileSize = fileSize,
                FileTransferTime = timeMs
            });
        }
    }
}
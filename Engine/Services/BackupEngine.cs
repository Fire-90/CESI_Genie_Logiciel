using System;
using System.Collections.Concurrent; // Nécessaire pour ConcurrentDictionary
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading; // Nécessaire pour ManualResetEvent
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

        // NOUVEAU : Dictionnaire pour stocker les événements de pause pour chaque tâche
        private readonly ConcurrentDictionary<string, ManualResetEvent> _jobPauseEvents = new ConcurrentDictionary<string, ManualResetEvent>();

        public BackupEngine(StateTracker stateTracker, ConfigManager configManager)
        {
            _stateTracker = stateTracker;
            _configManager = configManager;
        }

        // NOUVEAU : Méthode pour mettre en pause
        public void PauseJob(string jobName)
        {
            if (_jobPauseEvents.TryGetValue(jobName, out var pauseEvent))
            {
                pauseEvent.Reset(); // Ferme la barrière (met en pause)
            }
        }

        // NOUVEAU : Méthode pour reprendre
        public void ResumeJob(string jobName)
        {
            if (_jobPauseEvents.TryGetValue(jobName, out var pauseEvent))
            {
                pauseEvent.Set(); // Ouvre la barrière (relance)
            }
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

            int totalFilesToCopy = 0;
            long totalFilesSize = 0;

            string[] allFiles = Directory.GetFiles(job.SourceDirectory, "*.*", SearchOption.AllDirectories);
            foreach (string file in allFiles)
            {
                totalFilesToCopy++;
                totalFilesSize += new FileInfo(file).Length;
            }

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

            // NOUVEAU : Initialise l'événement de pause pour cette tâche (en position "Ouverte" / true)
            _jobPauseEvents[job.Name] = new ManualResetEvent(true);

            try
            {
                foreach (string file in allFiles)
                {
                    // NOUVEAU : C'est ici que la magie opère. 
                    // Si l'utilisateur clique sur pause, le thread va se figer à cette ligne jusqu'à ce qu'il clique sur "Play".
                    _jobPauseEvents[job.Name].WaitOne();

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
            finally
            {
                // NOUVEAU : Nettoyage de l'événement de pause pour libérer la mémoire à la fin de la sauvegarde
                if (_jobPauseEvents.TryRemove(job.Name, out var pauseEvent))
                {
                    pauseEvent.Dispose();
                }
            }
        }

        private async Task CopyFileWithLoggingAsync(string source, string target, string jobName, long fileSize)
        {
            // [Le reste de ta méthode CopyFileWithLoggingAsync ne change pas !]
            // ... (Conserve le contenu de ta méthode CopyFileWithLoggingAsync intact ici)
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
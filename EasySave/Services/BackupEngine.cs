using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EasySave.Models;
using EasyLog;

namespace EasySave.Services
{
    public class BackupEngine
    {
        public delegate void ProgressUpdateHandler(string currentFile, int remainingFiles);
        public event ProgressUpdateHandler OnProgressUpdate;

        private StateTracker _stateTracker;

        // Nom du processus métier à surveiller (sans le .exe). 
        // Note : "calculator" est l'application calculatrice standard de Windows
        private readonly string _businessSoftwareName = "calculator";

        public BackupEngine(StateTracker stateTracker)
        {
            _stateTracker = stateTracker;
        }

        // --- MÉTHODE DE DÉTECTION ---
        private bool IsBusinessSoftwareRunning()
        {
            // Vérifie dans les processus Windows si le logiciel métier est en cours d'exécution
            Process[] processes = Process.GetProcessesByName(_businessSoftwareName);
            return processes.Length > 0;
        }

        public async Task ExecuteJobAsync(BackupJob job)
        {
            // 1. Vérification AVANT de lancer le travail
            if (IsBusinessSoftwareRunning())
            {
                throw new Exception($"Lancement impossible : Le logiciel métier '{_businessSoftwareName}' est en cours d'exécution.");
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

            _stateTracker.UpdateState(job.Name, s =>
            {
                s.State = "ACTIVE";
                s.TotalFilesToCopy = totalFilesToCopy;
                s.TotalFilesSize = totalFilesSize;
                s.NbFilesLeftToDo = totalFilesToCopy;
                s.RemainingFilesSize = totalFilesSize;
                s.Progression = 0;
            });

            await ProcessDirectoryAsync(job.SourceDirectory, job.TargetDirectory, job);

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

        private async Task ProcessDirectoryAsync(string sourceDir, string targetDir, BackupJob job)
        {
            Directory.CreateDirectory(targetDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                // 2. Vérification PENDANT le travail (avant de traiter le prochain fichier)
                if (IsBusinessSoftwareRunning())
                {
                    // Le cahier des charges dit : "Dans le cas de travaux séquentiels, le logiciel doit terminer la sauvegarde du fichier en cours."
                    // On lance donc une exception pour casser la boucle proprement.
                    throw new Exception($"Sauvegarde interrompue : Détection du logiciel métier '{_businessSoftwareName}'.");
                }

                string targetFile = Path.Combine(targetDir, Path.GetFileName(file));
                bool shouldCopy = true;

                FileInfo sourceFileInfo = new FileInfo(file);

                if (job.Type == BackupType.Differential && File.Exists(targetFile))
                {
                    if (sourceFileInfo.LastWriteTime <= new FileInfo(targetFile).LastWriteTime)
                    {
                        shouldCopy = false;
                        _stateTracker.UpdateState(job.Name, s =>
                        {
                            s.NbFilesLeftToDo--;
                            s.RemainingFilesSize -= sourceFileInfo.Length;
                            s.Progression = s.TotalFilesToCopy > 0 ? (int)((double)(s.TotalFilesToCopy - s.NbFilesLeftToDo) / s.TotalFilesToCopy * 100) : 0;
                        });
                    }
                }

                if (shouldCopy) await CopyFileWithLoggingAsync(file, targetFile, job.Name, sourceFileInfo.Length);
            }

            foreach (string directory in Directory.GetDirectories(sourceDir))
            {
                await ProcessDirectoryAsync(directory, Path.Combine(targetDir, Path.GetFileName(directory)), job);
            }
        }

        private async Task CopyFileWithLoggingAsync(string source, string target, string jobName, long fileSize)
        {
            _stateTracker.UpdateState(jobName, s =>
            {
                s.SourceFilePath = source;
                s.TargetFilePath = target;
            });

            Stopwatch stopwatch = new Stopwatch();
            long timeMs = 0;

            try
            {
                stopwatch.Start();

                File.Copy(source, target, true);

                stopwatch.Stop();
                timeMs = stopwatch.ElapsedMilliseconds;

                _stateTracker.UpdateState(jobName, s =>
                {
                    s.NbFilesLeftToDo--;
                    s.RemainingFilesSize -= fileSize;
                    s.Progression = s.TotalFilesToCopy > 0 ? (int)((double)(s.TotalFilesToCopy - s.NbFilesLeftToDo) / s.TotalFilesToCopy * 100) : 0;
                });

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
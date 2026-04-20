using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using EasySave.Models;
using EasyLog;

namespace EasySave.Core
{
    public class BackupEngine
    {
        public delegate void ProgressUpdateHandler(string currentFile, int remainingFiles);
        public event ProgressUpdateHandler OnProgressUpdate; // Prêt pour le Binding MVVM futur

        public async Task ExecuteJobAsync(BackupJob job)
        {
            if (!Directory.Exists(job.SourceDirectory))
                throw new DirectoryNotFoundException($"Source directory not found: {job.SourceDirectory}");

            await ProcessDirectoryAsync(job.SourceDirectory, job.TargetDirectory, job);
        }

        private async Task ProcessDirectoryAsync(string sourceDir, string targetDir, BackupJob job)
        {
            Directory.CreateDirectory(targetDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string targetFile = Path.Combine(targetDir, Path.GetFileName(file));
                bool shouldCopy = true;

                FileInfo sourceFileInfo = new FileInfo(file);

                // Logique de sauvegarde différentielle
                if (job.Type == BackupType.Differential && File.Exists(targetFile))
                {
                    FileInfo targetFileInfo = new FileInfo(targetFile);
                    if (sourceFileInfo.LastWriteTime <= targetFileInfo.LastWriteTime)
                    {
                        shouldCopy = false;
                    }
                }

                if (shouldCopy)
                {
                    await CopyFileWithLoggingAsync(file, targetFile, job.Name, sourceFileInfo.Length);
                }
            }

            // Récursivité pour les sous-répertoires
            foreach (string directory in Directory.GetDirectories(sourceDir))
            {
                string targetSubDir = Path.Combine(targetDir, Path.GetFileName(directory));
                await ProcessDirectoryAsync(directory, targetSubDir, job);
            }
        }

        private async Task CopyFileWithLoggingAsync(string source, string target, string jobName, long fileSize)
        {
            Stopwatch stopwatch = new Stopwatch();
            long timeMs = 0;

            try
            {
                stopwatch.Start();
                // Simulation d'une copie asynchrone pour l'exemple (en production, utiliser FileStream)
                File.Copy(source, target, true);
                stopwatch.Stop();
                timeMs = stopwatch.ElapsedMilliseconds;

                // Déclenchement de l'événement pour l'interface utilisateur / Fichier d'état
                OnProgressUpdate?.Invoke(source, 0); // 0 est un placeholder pour les fichiers restants
            }
            catch (Exception)
            {
                stopwatch.Stop();
                timeMs = -1; // -1 en cas d'erreur selon le cahier des charges
            }

            // Appel de la DLL EasyLog
            var logEntry = new LogEntry
            {
                BackupName = jobName,
                SourceFile = source,
                TargetFile = target,
                FileSize = fileSize,
                TransferTimeMs = timeMs
            };

            await DailyLogger.Instance.WriteLogAsync(logEntry);
        }
    }
}
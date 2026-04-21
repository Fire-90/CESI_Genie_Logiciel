using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using EasySave.Models;

namespace EasySave.ViewModels
{
    public class BackupEngine
    {
        public delegate void ProgressUpdateHandler(string currentFile, int remainingFiles);
        public event ProgressUpdateHandler OnProgressUpdate;

        private StateTracker _stateTracker;

        public BackupEngine(StateTracker stateTracker)
        {
            _stateTracker = stateTracker;
        }

        public async Task ExecuteJobAsync(BackupJob job)
        {
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
                s.Progression = 0;
            });

            await ProcessDirectoryAsync(job.SourceDirectory, job.TargetDirectory, job);

            // Remise à zéro à la fin de la sauvegarde (comme dans l'exemple)
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
                    s.Progression = s.TotalFilesToCopy > 0 ? (int)((double)(s.TotalFilesToCopy - s.NbFilesLeftToDo) / s.TotalFilesToCopy * 100) : 0;
                });

                OnProgressUpdate?.Invoke(source, 0); // Event optionnel
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
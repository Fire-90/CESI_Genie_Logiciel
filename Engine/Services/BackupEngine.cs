using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using EasySave.Models;
using EasyLog;

namespace EasySave.Services
{
    public class BackupEngine
    {
        public delegate void ProgressUpdateHandler(string currentFile, int remainingFiles);
        public event ProgressUpdateHandler OnProgressUpdate;

        private readonly StateTracker _stateTracker;
        private readonly ConfigManager _configManager;

        public BackupEngine(StateTracker stateTracker, ConfigManager configManager)
        {
            _stateTracker = stateTracker;
            _configManager = configManager;
        }

        private string GetRunningBusinessSoftware()
        {
            var settings = _configManager.LoadSettings();
            if (settings.BusinessSoftwares == null) return null;

            foreach (string software in settings.BusinessSoftwares)
            {
                if (string.IsNullOrWhiteSpace(software)) continue;
                if (Process.GetProcessesByName(software).Length > 0) return software;
            }
            return null;
        }

        public async Task ExecuteJobAsync(BackupJob job)
        {
            string blocking = GetRunningBusinessSoftware();
            if (blocking != null) throw new Exception($"Logiciel bloquant détecté : {blocking}");

            if (!Directory.Exists(job.SourceDirectory)) throw new DirectoryNotFoundException("Source introuvable.");

            string[] allFiles = Directory.GetFiles(job.SourceDirectory, "*.*", SearchOption.AllDirectories);
            long totalSize = 0;
            foreach (string f in allFiles) totalSize += new FileInfo(f).Length;

            _stateTracker.UpdateState(job.Name, s =>
            {
                s.State = "ACTIVE";
                s.TotalFilesToCopy = allFiles.Length;
                s.TotalFilesSize = totalSize;
                s.NbFilesLeftToDo = allFiles.Length;
                s.RemainingFilesSize = totalSize;
            });

            await ProcessDirectoryAsync(job.SourceDirectory, job.TargetDirectory, job);

            _stateTracker.UpdateState(job.Name, s => { s.State = "END"; });
        }

        private async Task ProcessDirectoryAsync(string sourceDir, string targetDir, BackupJob job)
        {
            Directory.CreateDirectory(targetDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                if (GetRunningBusinessSoftware() != null) throw new Exception("Interruption : Logiciel métier lancé.");

                string targetFile = Path.Combine(targetDir, Path.GetFileName(file));
                FileInfo fi = new FileInfo(file);

                bool shouldCopy = true;
                if (job.Type == BackupType.Differential && File.Exists(targetFile))
                {
                    if (fi.LastWriteTime <= new FileInfo(targetFile).LastWriteTime) shouldCopy = false;
                }

                if (shouldCopy) await CopyFileWithLoggingAsync(file, targetFile, job);
            }

            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                await ProcessDirectoryAsync(dir, Path.Combine(targetDir, Path.GetFileName(dir)), job);
            }
        }

        private async Task CopyFileWithLoggingAsync(string source, string target, BackupJob job)
        {
            Stopwatch sw = new Stopwatch();
            var settings = _configManager.LoadSettings();
            bool shouldEncrypt = (settings.EncryptedExtensions ?? new List<string>())
                                 .Contains(Path.GetExtension(source).ToLower());

            sw.Start();
            if (shouldEncrypt)
            {
                await Task.Run(() => ExecuteCryptoSoft(source, target));
            }
            else
            {
                await Task.Run(() => File.Copy(source, target, true));
            }
            sw.Stop();

            long fileSize = new FileInfo(source).Length;

            _stateTracker.UpdateState(job.Name, s =>
            {
                s.NbFilesLeftToDo--;
                s.RemainingFilesSize -= fileSize;
                s.Progression = s.TotalFilesToCopy > 0 ? (int)((double)(s.TotalFilesToCopy - s.NbFilesLeftToDo) / s.TotalFilesToCopy * 100) : 0;
            });

            OnProgressUpdate?.Invoke(source, 0);

            // Appel modifié pour inclure l'ID du job et le format de log
            await DailyLogger.Instance.WriteLogAsync(new LogEntry
            {
                Name = job.Name,
                FileSource = source,
                FileTarget = target,
                FileSize = fileSize,
                FileTransferTime = sw.ElapsedMilliseconds
            }, job.Id.ToString(), settings.LogFormat);
        }

        private void ExecuteCryptoSoft(string source, string target)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CryptoSoft.exe");
            if (!File.Exists(path)) throw new FileNotFoundException("CryptoSoft.exe manquant.");

            ProcessStartInfo psi = new ProcessStartInfo(path, $"\"{source}\" \"{target}\"")
            { CreateNoWindow = true, UseShellExecute = false };

            using Process p = Process.Start(psi);
            p.WaitForExit();
        }
    }
}
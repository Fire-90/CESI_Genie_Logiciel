using System;
using System.Collections.Generic;
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
        private readonly ConfigManager _configManager;

        // Le constructeur prend désormais ConfigManager pour lire les paramètres dynamiquement
        public BackupEngine(StateTracker stateTracker, ConfigManager configManager)
        {
            _stateTracker = stateTracker;
            _configManager = configManager;
        }

        // --- DÉTECTION DYNAMIQUE DES LOGICIELS MÉTIER ---
        private string GetRunningBusinessSoftware()
        {
            var settings = _configManager.LoadSettings();
            var businessSoftwares = settings.BusinessSoftwares;

            if (businessSoftwares == null) return null;

            foreach (string software in businessSoftwares)
            {
                if (string.IsNullOrWhiteSpace(software)) continue;

                Process[] processes = Process.GetProcessesByName(software);
                if (processes.Length > 0)
                {
                    return software;
                }
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
                string blockingSoftware = GetRunningBusinessSoftware();
                if (blockingSoftware != null)
                {
                    throw new Exception($"Sauvegarde interrompue : Détection du logiciel '{blockingSoftware}'.");
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

            // Lecture des extensions à chiffrer
            var settings = _configManager.LoadSettings();
            List<string> encryptedExtensions = settings.EncryptedExtensions ?? new List<string>();
            string fileExtension = Path.GetExtension(source).ToLower();
            bool shouldEncrypt = encryptedExtensions.Contains(fileExtension);

            try
            {
                stopwatch.Start();

                if (shouldEncrypt)
                {
                    // --- CHIFFREMENT ---
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
                            if (process.ExitCode != 0)
                            {
                                throw new Exception("Erreur d'exécution de CryptoSoft.");
                            }
                        }
                    }
                    else
                    {
                        throw new FileNotFoundException("Exécutable CryptoSoft.exe introuvable.");
                    }
                }
                else
                {
                    // --- COPIE STANDARD ---
                    File.Copy(source, target, true);
                }

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
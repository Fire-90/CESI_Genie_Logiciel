using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace EasyLog
{
    public class LogEntry
    {
        public string Timestamp { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        public string BackupName { get; set; }
        public string SourceFile { get; set; }
        public string TargetFile { get; set; }
        public long FileSize { get; set; }
        public long TransferTimeMs { get; set; }
    }

    public sealed class DailyLogger
    {
        private static readonly Lazy<DailyLogger> _instance = new Lazy<DailyLogger>(() => new DailyLogger());
        public static DailyLogger Instance => _instance.Value;

        private readonly string _logDirectory;
        private static readonly object _lockObj = new object();

        private DailyLogger()
        {
            // Utilisation de CommonApplicationData (ex: C:\ProgramData\EasySave\Logs)
            _logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EasySave", "Logs");
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }
        }

        public async Task WriteLogAsync(LogEntry entry)
        {
            string fileName = $"{DateTime.Now:yyyy-MM-dd}.json";
            string filePath = Path.Combine(_logDirectory, fileName);

            var options = new JsonSerializerOptions { WriteIndented = true };
            string jsonString = JsonSerializer.Serialize(entry, options);

            // Verrou pour éviter les conflits d'écriture si plusieurs threads de sauvegarde tournent
            lock (_lockObj)
            {
                // Ajout d'un retour à la ligne pour la lisibilité Notepad comme demandé
                File.AppendAllText(filePath, jsonString + Environment.NewLine);
            }
            await Task.CompletedTask;
        }
    }
}
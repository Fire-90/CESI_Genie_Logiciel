using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace EasyLog
{
    public class LogEntry
    {
        [JsonPropertyName("Name")]
        public string Name { get; set; }

        [JsonPropertyName("FileSource")]
        public string FileSource { get; set; }

        [JsonPropertyName("FileTarget")]
        public string FileTarget { get; set; }

        [JsonPropertyName("FileSize")]
        public long FileSize { get; set; }

        [JsonPropertyName("FileTransferTime")]
        public double FileTransferTime { get; set; }

        [JsonPropertyName("time")]
        public string Time { get; set; } = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
    }

    public sealed class DailyLogger
    {
        private static readonly Lazy<DailyLogger> _instance = new Lazy<DailyLogger>(() => new DailyLogger());
        public static DailyLogger Instance => _instance.Value;

        private readonly string _logDirectory;
        private static readonly object _lockObj = new object();

        private DailyLogger()
        {
            // Création du dossier "logs" dans le même répertoire que le .exe
            string exePath = AppDomain.CurrentDomain.BaseDirectory;
            _logDirectory = Path.Combine(exePath, "data", "logs");

            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }
        }

        public async Task WriteLogAsync(LogEntry entry)
        {
            string fileName = $"{DateTime.Now:yyyy-MM-dd}.json";
            string filePath = Path.Combine(_logDirectory, fileName);

            lock (_lockObj)
            {
                List<LogEntry> logs = new List<LogEntry>();

                // Lecture du fichier existant pour conserver un tableau JSON valide
                if (File.Exists(filePath))
                {
                    try
                    {
                        string existingJson = File.ReadAllText(filePath);
                        if (!string.IsNullOrWhiteSpace(existingJson))
                        {
                            logs = JsonSerializer.Deserialize<List<LogEntry>>(existingJson) ?? new List<LogEntry>();
                        }
                    }
                    catch (JsonException) { /* Fichier ignoré si corrompu */ }
                }

                logs.Add(entry);

                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(logs, options);

                File.WriteAllText(filePath, jsonString);
            }

            await Task.CompletedTask;
        }
    }
}
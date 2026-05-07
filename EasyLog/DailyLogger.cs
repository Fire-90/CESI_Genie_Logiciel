using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace EasyLog
{
    [Serializable]
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

        private readonly string _baseDataDirectory;
        private readonly string _settingsFilePath;
        private static readonly object _lockObj = new object();

        // Événement déclenché à chaque nouveau log généré (JobId, Format, Entry)
        public static event Action<string, string, LogEntry> OnLogGenerated;

        private DailyLogger()
        {
            string exePath = AppDomain.CurrentDomain.BaseDirectory;
            _baseDataDirectory = Path.Combine(exePath, "Data");
            _settingsFilePath = Path.Combine(exePath, "Data", "settings.json");
        }

        private string GetCurrentLogFormat()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        if (doc.RootElement.TryGetProperty("LogFormat", out JsonElement formatElement))
                        {
                            return formatElement.GetString() ?? "json";
                        }
                    }
                }
            }
            catch { }
            return "json";
        }

        public async Task WriteLogAsync(LogEntry entry, string jobId, string logFormat = null)
        {
            string currentFormat = logFormat ?? GetCurrentLogFormat();

            // Le chemin cible pointe désormais toujours vers le dossier "logs" global
            string logDirectory = Path.Combine(_baseDataDirectory, "logs");

            lock (_lockObj)
            {
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                if (currentFormat.Equals("xml", StringComparison.OrdinalIgnoreCase))
                {
                    WriteXmlLog(entry, logDirectory);
                }
                else
                {
                    WriteJsonLog(entry, logDirectory);
                }
            }

            // Avertissement pour la transmission réseau (le jobId est conservé pour le serveur)
            OnLogGenerated?.Invoke(jobId, currentFormat, entry);

            await Task.CompletedTask;
        }

        private void WriteJsonLog(LogEntry entry, string logDirectory)
        {
            string fileName = $"{DateTime.Now:yyyy-MM-dd}.json";
            string filePath = Path.Combine(logDirectory, fileName);
            List<LogEntry> logs = new List<LogEntry>();

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
                catch (JsonException) { }
            }

            logs.Add(entry);
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(filePath, JsonSerializer.Serialize(logs, options));
        }

        private void WriteXmlLog(LogEntry entry, string logDirectory)
        {
            string fileName = $"{DateTime.Now:yyyy-MM-dd}.xml";
            string filePath = Path.Combine(logDirectory, fileName);
            List<LogEntry> logs = new List<LogEntry>();
            XmlSerializer serializer = new XmlSerializer(typeof(List<LogEntry>));

            if (File.Exists(filePath))
            {
                try
                {
                    using (FileStream fs = new FileStream(filePath, FileMode.Open))
                    {
                        logs = (List<LogEntry>)serializer.Deserialize(fs);
                    }
                }
                catch (InvalidOperationException) { }
            }

            logs.Add(entry);
            using (FileStream fs = new FileStream(filePath, FileMode.Create))
            {
                serializer.Serialize(fs, logs);
            }
        }
    }
}
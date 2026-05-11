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

        private readonly string _logDirectory;
        private readonly string _settingsFilePath;
        private static readonly object _lockObj = new object();

        private DailyLogger()
        {
            string exePath = AppDomain.CurrentDomain.BaseDirectory;
            _logDirectory = Path.Combine(exePath, "data", "logs");
            _settingsFilePath = Path.Combine(exePath, "data", "settings.json");

            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }
        }

        private string GetCurrentLogFormat()
        {
            int maxRetries = 5;
            for (int i = 0; i < maxRetries; i++)
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
                    break;
                }
                catch (IOException)
                {
                    System.Threading.Thread.Sleep(50);
                }
                catch
                {
                    break; // Fichier mal formaté, on arrête d'essayer
                }
            }
            return "json";
        }

        public async Task WriteLogAsync(LogEntry entry)
        {
            string currentFormat = GetCurrentLogFormat();

            lock (_lockObj)
            {
                if (currentFormat.Equals("xml", StringComparison.OrdinalIgnoreCase))
                {
                    WriteXmlLog(entry);
                }
                else
                {
                    WriteJsonLog(entry);
                }
            }

            await Task.CompletedTask;
        }

        private void WriteJsonLog(LogEntry entry)
        {
            string fileName = $"{DateTime.Now:yyyy-MM-dd}.json";
            string filePath = Path.Combine(_logDirectory, fileName);
            List<LogEntry> logs = new List<LogEntry>();

            int maxRetries = 5;
            for (int r = 0; r < maxRetries; r++)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        string existingJson = File.ReadAllText(filePath);
                        if (!string.IsNullOrWhiteSpace(existingJson))
                        {
                            logs = JsonSerializer.Deserialize<List<LogEntry>>(existingJson) ?? new List<LogEntry>();
                        }
                    }

                    logs.Add(entry);

                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string jsonString = JsonSerializer.Serialize(logs, options);

                    File.WriteAllText(filePath, jsonString);
                    break; // Succès, on sort de la boucle
                }
                catch (IOException)
                {
                    if (r == maxRetries - 1) throw;
                    System.Threading.Thread.Sleep(50);
                }
                catch (JsonException) { break; } // Corrompu, on abandonne
            }
        }

        private void WriteXmlLog(LogEntry entry)
        {
            string fileName = $"{DateTime.Now:yyyy-MM-dd}.xml";
            string filePath = Path.Combine(_logDirectory, fileName);
            List<LogEntry> logs = new List<LogEntry>();
            XmlSerializer serializer = new XmlSerializer(typeof(List<LogEntry>));

            int maxRetries = 5;
            for (int r = 0; r < maxRetries; r++)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        using (FileStream fs = new FileStream(filePath, FileMode.Open))
                        {
                            logs = (List<LogEntry>)serializer.Deserialize(fs);
                        }
                    }

                    logs.Add(entry);

                    using (FileStream fs = new FileStream(filePath, FileMode.Create))
                    {
                        serializer.Serialize(fs, logs);
                    }
                    break; // Succès, on sort
                }
                catch (IOException)
                {
                    if (r == maxRetries - 1) throw;
                    System.Threading.Thread.Sleep(50);
                }
                catch (InvalidOperationException) { break; } // Corrompu, on abandonne
            }
        }
    }
}
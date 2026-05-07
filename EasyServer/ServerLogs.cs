using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace EasyServer
{
    [Serializable]
    public class ServerLogEntry
    {
        [JsonPropertyName("Time")]
        public string Time { get; set; } = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");

        [JsonPropertyName("ClientId")]
        public string ClientId { get; set; }

        [JsonPropertyName("Action")]
        public string Action { get; set; }

        [JsonPropertyName("Message")]
        public string Message { get; set; }
    }

    [Serializable]
    public class ClientLogEntry
    {
        public string Name { get; set; }
        public string FileSource { get; set; }
        public string FileTarget { get; set; }
        public long FileSize { get; set; }
        public double FileTransferTime { get; set; }
        public string time { get; set; }
    }

    public sealed class ServerLogger
    {
        private static readonly Lazy<ServerLogger> _instance = new Lazy<ServerLogger>(() => new ServerLogger());
        public static ServerLogger Instance => _instance.Value;

        private readonly string _baseDataDirectory;
        private static readonly object _lockObj = new object();

        private ServerLogger()
        {
            _baseDataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
        }

        public async Task WriteConnectionLogAsync(ServerLogEntry entry, string clientId)
        {
            string safeClientId = clientId.Replace(":", "-");
            entry.ClientId = safeClientId;

            string filePath = Path.Combine(_baseDataDirectory, "connection_logs.json");

            lock (_lockObj)
            {
                if (!Directory.Exists(_baseDataDirectory))
                {
                    Directory.CreateDirectory(_baseDataDirectory);
                }

                List<ServerLogEntry> logs = new List<ServerLogEntry>();
                if (File.Exists(filePath))
                {
                    try
                    {
                        string existingJson = File.ReadAllText(filePath);
                        if (!string.IsNullOrWhiteSpace(existingJson))
                        {
                            logs = JsonSerializer.Deserialize<List<ServerLogEntry>>(existingJson) ?? new List<ServerLogEntry>();
                        }
                    }
                    catch (JsonException) { }
                }

                logs.Add(entry);
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(filePath, JsonSerializer.Serialize(logs, options));
            }

            await Task.CompletedTask;
        }

        public async Task WriteClientBackupLogAsync(string jsonPayload, string clientId, string jobId, string format)
        {
            string safeClientId = clientId.Replace(":", "-");
            string logDirectory = Path.Combine(_baseDataDirectory, safeClientId, "logs");

            lock (_lockObj)
            {
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                try
                {
                    var newLog = JsonSerializer.Deserialize<ClientLogEntry>(jsonPayload);
                    if (newLog == null) return;

                    if (format.Equals("xml", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteXmlClientLog(newLog, logDirectory);
                    }
                    else
                    {
                        WriteJsonClientLog(newLog, logDirectory);
                    }
                }
                catch (Exception) { }
            }

            await Task.CompletedTask;
        }

        private void WriteJsonClientLog(ClientLogEntry entry, string logDirectory)
        {
            string filePath = Path.Combine(logDirectory, $"{DateTime.Now:yyyy-MM-dd}.json");
            List<ClientLogEntry> logs = new List<ClientLogEntry>();

            if (File.Exists(filePath))
            {
                try { logs = JsonSerializer.Deserialize<List<ClientLogEntry>>(File.ReadAllText(filePath)) ?? new List<ClientLogEntry>(); }
                catch (JsonException) { }
            }

            logs.Add(entry);
            File.WriteAllText(filePath, JsonSerializer.Serialize(logs, new JsonSerializerOptions { WriteIndented = true }));
        }

        private void WriteXmlClientLog(ClientLogEntry entry, string logDirectory)
        {
            string filePath = Path.Combine(logDirectory, $"{DateTime.Now:yyyy-MM-dd}.xml");
            List<ClientLogEntry> logs = new List<ClientLogEntry>();
            XmlSerializer serializer = new XmlSerializer(typeof(List<ClientLogEntry>));

            if (File.Exists(filePath))
            {
                try
                {
                    using (FileStream fs = new FileStream(filePath, FileMode.Open)) { logs = (List<ClientLogEntry>)serializer.Deserialize(fs); }
                }
                catch (InvalidOperationException) { }
            }

            logs.Add(entry);
            using (FileStream fs = new FileStream(filePath, FileMode.Create)) { serializer.Serialize(fs, logs); }
        }
    }
}
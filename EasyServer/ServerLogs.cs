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
            string clientDir = Path.Combine(_baseDataDirectory, clientId.Replace(":", "-"), "logs");
            if (!Directory.Exists(clientDir)) Directory.CreateDirectory(clientDir);

            string filePath = Path.Combine(clientDir, "connection_history.json");

            lock (_lockObj)
            {
                List<ServerLogEntry> logs = new List<ServerLogEntry>();
                if (File.Exists(filePath))
                {
                    try { logs = JsonSerializer.Deserialize<List<ServerLogEntry>>(File.ReadAllText(filePath)) ?? new List<ServerLogEntry>(); }
                    catch { }
                }
                logs.Add(entry);
                File.WriteAllText(filePath, JsonSerializer.Serialize(logs, new JsonSerializerOptions { WriteIndented = true }));
            }
            await Task.CompletedTask;
        }

        // --- MÉTHODE MANQUANTE AJOUTÉE ICI ---
        public async Task WriteClientLogAsync(string jsonEntry, string clientId, string jobId, string format)
        {
            string clientDir = Path.Combine(_baseDataDirectory, clientId.Replace(":", "-"), "logs", $"job_{jobId}");
            if (!Directory.Exists(clientDir)) Directory.CreateDirectory(clientDir);

            try
            {
                var entry = JsonSerializer.Deserialize<ClientLogEntry>(jsonEntry);
                if (entry == null) return;

                if (format.ToLower() == "xml")
                    WriteXmlClientLog(entry, clientDir);
                else
                    WriteJsonClientLog(entry, clientDir);
            }
            catch { }
            await Task.CompletedTask;
        }

        private void WriteJsonClientLog(ClientLogEntry entry, string logDirectory)
        {
            string filePath = Path.Combine(logDirectory, $"{DateTime.Now:yyyy-MM-dd}.json");
            lock (_lockObj)
            {
                List<ClientLogEntry> logs = new List<ClientLogEntry>();
                if (File.Exists(filePath))
                {
                    try { logs = JsonSerializer.Deserialize<List<ClientLogEntry>>(File.ReadAllText(filePath)) ?? new List<ClientLogEntry>(); }
                    catch { }
                }
                logs.Add(entry);
                File.WriteAllText(filePath, JsonSerializer.Serialize(logs, new JsonSerializerOptions { WriteIndented = true }));
            }
        }

        private void WriteXmlClientLog(ClientLogEntry entry, string logDirectory)
        {
            string filePath = Path.Combine(logDirectory, $"{DateTime.Now:yyyy-MM-dd}.xml");
            lock (_lockObj)
            {
                List<ClientLogEntry> logs = new List<ClientLogEntry>();
                XmlSerializer serializer = new XmlSerializer(typeof(List<ClientLogEntry>));

                if (File.Exists(filePath))
                {
                    try
                    {
                        using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            logs = (List<ClientLogEntry>)serializer.Deserialize(fs);
                        }
                    }
                    catch { }
                }

                logs.Add(entry);
                using (FileStream fs = new FileStream(filePath, FileMode.Create))
                {
                    serializer.Serialize(fs, logs);
                }
            }
        }
    }
}
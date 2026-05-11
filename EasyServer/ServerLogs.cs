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
        public string JobId { get; set; } // Identifiant du travail (Job)
        public string Name { get; set; }  // Nom du travail
        public string FileSource { get; set; }
        public string FileTarget { get; set; }
        public long FileSize { get; set; }
        public double FileTransferTime { get; set; }
        public double EncryptionTime { get; set; } // Temps de chiffrement (en ms)
        public string time { get; set; } // Horodatage fourni par le client
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
            if (!Directory.Exists(_baseDataDirectory)) Directory.CreateDirectory(_baseDataDirectory);
        }

        /// <summary>
        /// Écrit dans le journal de connexion global : data/connection_history.json
        /// </summary>
        public async Task WriteConnectionLogAsync(ServerLogEntry entry, string clientId)
        {
            entry.ClientId = clientId;
            string filePath = Path.Combine(_baseDataDirectory, "connection_history.json");

            lock (_lockObj)
            {
                List<ServerLogEntry> logs = new List<ServerLogEntry>();
                if (File.Exists(filePath))
                {
                    try
                    {
                        logs = JsonSerializer.Deserialize<List<ServerLogEntry>>(File.ReadAllText(filePath)) ?? new List<ServerLogEntry>();
                    }
                    catch { }
                }
                logs.Add(entry);
                File.WriteAllText(filePath, JsonSerializer.Serialize(logs, new JsonSerializerOptions { WriteIndented = true }));
            }
            await Task.CompletedTask;
        }

        /// <summary>
        /// Écrit les logs de sauvegarde dans le dossier du client : data/ID/logs/
        /// Tous les jobs du client sont regroupés dans le fichier du jour.
        /// </summary>
        public async Task WriteClientLogAsync(string jsonEntry, string clientId, string jobId, string format)
        {
            string safeClientId = clientId.Replace(":", "-");
            string clientLogDir = Path.Combine(_baseDataDirectory, safeClientId, "logs");

            if (!Directory.Exists(clientLogDir)) Directory.CreateDirectory(clientLogDir);

            try
            {
                var entry = JsonSerializer.Deserialize<ClientLogEntry>(jsonEntry);
                if (entry == null) return;

                // On injecte le JobId qui manque souvent dans le JSON brut du client
                entry.JobId = jobId;

                if (format.ToLower() == "xml")
                    WriteXmlClientLog(entry, clientLogDir);
                else
                    WriteJsonClientLog(entry, clientLogDir);
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
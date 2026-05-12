using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace EasyServer
{
    public sealed class ServerStateManager
    {
        private static readonly Lazy<ServerStateManager> _instance = new Lazy<ServerStateManager>(() => new ServerStateManager());
        public static ServerStateManager Instance => _instance.Value;

        private readonly string _baseDataDirectory;
        private static readonly object _lockObj = new object();

        private ServerStateManager()
        {
            _baseDataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
        }

        public async Task WriteClientStateAsync(string jsonPayload, string clientId)
        {
            string safeClientId = clientId.Replace(":", "-");
            string clientDirectory = Path.Combine(_baseDataDirectory, safeClientId);
            string filePath = Path.Combine(clientDirectory, "state.json");

            lock (_lockObj)
            {
                if (!Directory.Exists(clientDirectory))
                {
                    Directory.CreateDirectory(clientDirectory);
                }

                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(jsonPayload))
                    {
                        var options = new JsonSerializerOptions { WriteIndented = true };
                        string formattedJson = JsonSerializer.Serialize(doc.RootElement, options);
                        File.WriteAllText(filePath, formattedJson);
                    }
                }
                catch (Exception)
                {
                    File.WriteAllText(filePath, jsonPayload);
                }
            }

            await Task.CompletedTask;
        }

        public async Task<string> GetAllClientStatesAsync(string excludeClientId, List<string> activeClientIds)
        {
            var states = new Dictionary<string, object>();

            if (Directory.Exists(_baseDataDirectory))
            {
                foreach (var dir in Directory.GetDirectories(_baseDataDirectory))
                {
                    string clientId = Path.GetFileName(dir);

                    if (clientId == excludeClientId || !activeClientIds.Contains(clientId)) continue;

                    string statePath = Path.Combine(dir, "state.json");

                    if (File.Exists(statePath))
                    {
                        try
                        {
                            string json = await File.ReadAllTextAsync(statePath);
                            states[clientId] = JsonDocument.Parse(json).RootElement;
                        }
                        catch { }
                    }
                }
            }

            return JsonSerializer.Serialize(states);
        }
    }
}
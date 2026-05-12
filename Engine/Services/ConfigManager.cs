using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using EasySave.Models;

namespace EasySave.Services
{
    public class ConfigManager
    {
        private readonly string _settingsFilePath;
        private static readonly object _lockObj = new object();

        public ConfigManager()
        {
            string dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            if (!Directory.Exists(dataPath)) Directory.CreateDirectory(dataPath);

            _settingsFilePath = Path.Combine(dataPath, "settings.json");
        }

        public AppSettings LoadSettings()
        {
            lock (_lockObj)
            {
                if (!File.Exists(_settingsFilePath))
                {
                    return CreateDefaultSettings();
                }

                int maxRetries = 5;
                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        string json = File.ReadAllText(_settingsFilePath);
                        return JsonSerializer.Deserialize<AppSettings>(json) ?? CreateDefaultSettings();
                    }
                    catch (IOException)
                    {
                        if (i == maxRetries - 1) throw;
                        Thread.Sleep(50);
                    }
                    catch (JsonException)
                    {
                        return CreateDefaultSettings();
                    }
                }

                return CreateDefaultSettings();
            }
        }

        private AppSettings CreateDefaultSettings()
        {
            var settings = new AppSettings();
            for (int i = 1; i <= 5; i++)
            {
                settings.Jobs.Add(new BackupJob(i, $"Save{i}", "", "", BackupType.Full));
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(_settingsFilePath, json);

            return settings;
        }

        public void SaveSettings(AppSettings settings)
        {
            lock (_lockObj)
            {
                if (settings.Jobs == null || settings.Jobs.Count == 0)
                {
                    if (File.Exists(_settingsFilePath))
                    {
                        try
                        {
                            string existingJson = File.ReadAllText(_settingsFilePath);
                            var existingSettings = JsonSerializer.Deserialize<AppSettings>(existingJson);
                            if (existingSettings != null && existingSettings.Jobs != null && existingSettings.Jobs.Count > 0)
                            {
                                settings.Jobs = existingSettings.Jobs;
                            }
                        }
                        catch (JsonException) { }
                    }
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(settings, options);

                int maxRetries = 5;
                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        File.WriteAllText(_settingsFilePath, json);
                        break;
                    }
                    catch (IOException)
                    {
                        if (i == maxRetries - 1) throw;
                        Thread.Sleep(50);
                    }
                }
            }
        }
    }

    public class AppSettings
    {
        public string Language { get; set; } = "FR";
        public string LogFormat { get; set; } = "json";
        public string LogDestination { get; set; } = "LocalAndServer";
        public string ServerIP { get; set; } = "127.0.0.1";
        public string ClientName { get; set; } = "Client-" + Environment.MachineName;

        public long MaxParallelFileSizeLimit { get; set; } = 500;
        public string MaxParallelFileSizeLimitUnit { get; set; } = "Mo";
        public string EncryptionKey { get; set; } = "EasySaveKey";

        public List<BackupJob> Jobs { get; set; } = new List<BackupJob>();
        public List<string> BusinessSoftwares { get; set; } = new List<string> { "CalculatorApp", "notepad" };
        public List<string> EncryptedExtensions { get; set; } = new List<string> { ".txt", ".docx" };
        public List<string> PriorityExtensions { get; set; } = new List<string> { ".xml", ".txt" };
    }
}
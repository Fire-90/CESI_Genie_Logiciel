using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using EasySave.Models;

namespace EasySave.Services
{
    public class ConfigManager
    {
        private readonly string _settingsFilePath;

        public ConfigManager()
        {
            string dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            if (!Directory.Exists(dataPath)) Directory.CreateDirectory(dataPath);

            _settingsFilePath = Path.Combine(dataPath, "settings.json");
        }

        public AppSettings LoadSettings()
        {
            if (!File.Exists(_settingsFilePath))
            {
                return CreateDefaultSettings();
            }

            try
            {
                string json = File.ReadAllText(_settingsFilePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? CreateDefaultSettings();
            }
            catch (JsonException)
            {
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
            File.WriteAllText(_settingsFilePath, json);
        }
    }

    public class AppSettings
    {
        public string Language { get; set; } = "FR";
        public string LogFormat { get; set; } = "json";
        public string LogDestination { get; set; } = "LocalAndServer";
        public string ServerIP { get; set; } = "127.0.0.1";

        // Nouvel Identifiant Client
        public string ClientName { get; set; } = "Client-" + Environment.MachineName;

        public List<BackupJob> Jobs { get; set; } = new List<BackupJob>();
        public List<string> BusinessSoftwares { get; set; } = new List<string> { "calculator", "notepad" };
        public List<string> EncryptedExtensions { get; set; } = new List<string> { ".txt", ".docx" };

        // Defines priority file extensions
        public List<string> PriorityExtensions { get; set; } = new List<string> { ".xml", ".txt" };

    }
}
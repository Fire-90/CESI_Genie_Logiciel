using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using EasySave.Models;

namespace EasySave.Controller
{
    // Nouvelle classe pour englober toute la configuration
    public class AppSettings
    {
        public string LogFormat { get; set; } = "json";
        public List<BackupJob> Jobs { get; set; } = new List<BackupJob>();
    }

    public class ConfigManager
    {
        private readonly string _configFilePath;

        public ConfigManager()
        {
            string dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            if (!Directory.Exists(dataPath)) Directory.CreateDirectory(dataPath);

            _configFilePath = Path.Combine(dataPath, "config.json");
        }

        public AppSettings LoadConfig()
        {
            if (!File.Exists(_configFilePath))
            {
                return CreateDefaultConfig();
            }

            try
            {
                string json = File.ReadAllText(_configFilePath);
                // On tente de désérialiser vers le nouveau format
                return JsonSerializer.Deserialize<AppSettings>(json) ?? CreateDefaultConfig();
            }
            catch (JsonException)
            {
                // Si l'ancien fichier config.json ne contenait qu'un tableau, 
                // cette exception permet de recréer une configuration propre.
                return CreateDefaultConfig();
            }
        }

        private AppSettings CreateDefaultConfig()
        {
            var settings = new AppSettings();
            for (int i = 1; i <= 5; i++)
            {
                settings.Jobs.Add(new BackupJob(i, $"Save{i}", "", "", BackupType.Full));
            }
            SaveConfig(settings);
            return settings;
        }

        public void SaveConfig(AppSettings settings)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(_configFilePath, json);
        }
    }
}
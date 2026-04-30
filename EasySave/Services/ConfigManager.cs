using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using EasySave.Models;

namespace EasySave.Services
{
    public class ConfigManager
    {
        private readonly string _configFilePath;
        private readonly string _settingsFilePath;

        public ConfigManager()
        {
            string dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            if (!Directory.Exists(dataPath)) Directory.CreateDirectory(dataPath);

            _configFilePath = Path.Combine(dataPath, "config.json");
            _settingsFilePath = Path.Combine(dataPath, "settings.json");
        }

        // --- GESTION DES TRAVAUX
        public List<BackupJob> LoadConfig()
        {
            if (!File.Exists(_configFilePath))
            {
                return CreateDefaultConfig();
            }

            string json = File.ReadAllText(_configFilePath);
            return JsonSerializer.Deserialize<List<BackupJob>>(json) ?? CreateDefaultConfig();
        }

        private List<BackupJob> CreateDefaultConfig()
        {
            var defaultJobs = new List<BackupJob>();
            for (int i = 1; i <= 5; i++)
            {
                // Création de 5 emplacements vides par défaut
                defaultJobs.Add(new BackupJob(i, $"Save{i}", "", "", BackupType.Full));
            }
            SaveConfig(defaultJobs);
            return defaultJobs;
        }

        public void SaveConfig(List<BackupJob> jobs)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(jobs, options);
            File.WriteAllText(_configFilePath, json);
        }

        // --- GESTION DES PARAMÈTRES (Nouvel ajout) ---

        public AppSettings LoadSettings()
        {
            if (!File.Exists(_settingsFilePath))
            {
                return new AppSettings(); // Retourne les valeurs par défaut
            }

            string json = File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }

        public void SaveSettings(AppSettings settings)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(_settingsFilePath, json);
        }
    }

    // --- CLASSE DE PARAMÈTRES (Intégrée ici pour ne pas créer de nouveau fichier) ---
    public class AppSettings
    {
        public string Language { get; set; } = "FR";
        public string LogFormat { get; set; } = "JSON";
        public List<string> BusinessSoftwares { get; set; } = new List<string> { "calculator", "notepad" };
        public List<string> EncryptedExtensions { get; set; } = new List<string> { ".txt", ".docx" };
    }
}
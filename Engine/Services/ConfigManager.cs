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
                // Création de 5 emplacements vides par défaut
                settings.Jobs.Add(new BackupJob(i, $"Save{i}", "", "", BackupType.Full));
            }
            // Enregistrement initial forcé sans la protection de liste vide
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(_settingsFilePath, json);

            return settings;
        }

        public void SaveSettings(AppSettings settings)
        {
            // Protection contre l'effacement accidentel des travaux (Jobs)
            // Si la liste des travaux reçue est vide, on récupère celle déjà sauvegardée.
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
                            // Restauration des travaux existants
                            settings.Jobs = existingSettings.Jobs;
                        }
                    }
                    catch (JsonException)
                    {
                        // En cas d'erreur de lecture, on ignore pour ne pas bloquer l'application
                    }
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
        public string LogFormat { get; set; } = "JSON";
        public List<string> BusinessSoftwares { get; set; } = new List<string> { "calculator", "notepad" };
        public List<string> EncryptedExtensions { get; set; } = new List<string> { ".txt", ".docx" };

        // Defines priority file extensions
        public List<string> PriorityExtensions { get; set; } = new List<string> { ".xml", ".txt" };

        public List<BackupJob> Jobs { get; set; } = new List<BackupJob>();
    }
}
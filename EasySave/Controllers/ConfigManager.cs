using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using EasySave.Models;

namespace EasySave.ViewModels
{
    public class ConfigManager
    {
        private readonly string _configFilePath;

        public ConfigManager()
        {
            string dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            if (!Directory.Exists(dataPath)) Directory.CreateDirectory(dataPath);

            _configFilePath = Path.Combine(dataPath, "config.json");
        }

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

        public void DeleteConfig(BackupJob job)
        {
            var jobs = new List<BackupJob>();
            if (job != null)
            {
                job.Name = $"Save{job.Id}";
                job.SourceDirectory = "";
                job.TargetDirectory = "";
                job.Type = BackupType.Full;
                SaveConfig(jobs);
            }

        }

        public void SaveConfig(List<BackupJob> jobs)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(jobs, options);
            File.WriteAllText(_configFilePath, json);
        }
    }
}
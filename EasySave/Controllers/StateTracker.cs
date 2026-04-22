using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using EasySave.Models;

namespace EasySave.Controller
{
    public class JobStateInfo
    {
        [JsonPropertyName("Name")]
        public string Name { get; set; }
        [JsonPropertyName("SourceFilePath")]
        public string SourceFilePath { get; set; }
        [JsonPropertyName("TargetFilePath")]
        public string TargetFilePath { get; set; }
        [JsonPropertyName("State")]
        public string State { get; set; }
        [JsonPropertyName("TotalFilesToCopy")]
        public int TotalFilesToCopy { get; set; }
        [JsonPropertyName("TotalFilesSize")]
        public long TotalFilesSize { get; set; }
        [JsonPropertyName("NbFilesLeftToDo")]
        public int NbFilesLeftToDo { get; set; }
        [JsonPropertyName("Progression")]
        public int Progression { get; set; }
    }

    public class StateTracker
    {
        private readonly string _stateFilePath;
        private static readonly object _lockObj = new object();
        private List<JobStateInfo> _currentStates;

        public StateTracker(List<BackupJob> configuredJobs)
        {
            string dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            if (!Directory.Exists(dataPath)) Directory.CreateDirectory(dataPath);

            _stateFilePath = Path.Combine(dataPath, "state.json");
            InitializeStates(configuredJobs);
        }

        private void InitializeStates(List<BackupJob> jobs)
        {
            _currentStates = new List<JobStateInfo>();
            foreach (var job in jobs)
            {
                _currentStates.Add(new JobStateInfo
                {
                    Name = job.Name,
                    SourceFilePath = "",
                    TargetFilePath = "",
                    State = "END",
                    TotalFilesToCopy = 0,
                    TotalFilesSize = 0,
                    NbFilesLeftToDo = 0,
                    Progression = 0
                });
            }
            WriteAllStates();
        }

        public void UpdateState(string jobName, Action<JobStateInfo> updateAction)
        {
            lock (_lockObj)
            {
                var state = _currentStates.FirstOrDefault(s => s.Name == jobName);
                if (state != null)
                {
                    updateAction(state);
                    WriteAllStates();
                }
            }
        }

        public void UpdateJobName(string oldName, string newName)
        {
            lock (_lockObj)
            {
                var state = _currentStates.FirstOrDefault(s => s.Name == oldName);
                if (state != null)
                {
                    state.Name = newName;
                    WriteAllStates();
                }
            }
        }

        private void WriteAllStates()
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string jsonString = JsonSerializer.Serialize(_currentStates, options);
            File.WriteAllText(_stateFilePath, jsonString);
        }
    }
}
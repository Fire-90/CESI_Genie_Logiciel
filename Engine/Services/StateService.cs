using EasySave.Models;
using System.Text.Json;

namespace EasySave.Services
{

    public class StateService
    {
        private readonly string _stateFilePath;
        private static readonly object _lockObj = new object();
        private List<JobStateInfo> _currentStates;

        // Observer
        public static event Action<string> OnStateUpdated;

        public StateService(List<BackupJob> configuredJobs)
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
                    State = "INACTIVE",
                    TotalFilesToCopy = 0,
                    TotalFilesSize = 0,
                    NbFilesLeftToDo = 0,
                    Progression = 0,
                    CurrentSpeed = "",
                    LastActionDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"),
                    RemainingFilesSize = 0
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
                    state.LastActionDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
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

        public void BroadcastState()
        {
            lock (_lockObj)
            {
                if (_currentStates != null)
                {
                    string compactJson = JsonSerializer.Serialize(_currentStates);
                    OnStateUpdated?.Invoke(compactJson);
                }
            }
        }

        private void WriteAllStates()
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string indentedJson = JsonSerializer.Serialize(_currentStates, options);
            File.WriteAllText(_stateFilePath, indentedJson);

            string compactJson = JsonSerializer.Serialize(_currentStates);
            OnStateUpdated?.Invoke(compactJson);
        }
    }
}
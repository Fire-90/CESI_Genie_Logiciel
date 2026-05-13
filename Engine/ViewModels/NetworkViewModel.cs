using EasySave.Models;
using EasySave.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace EasySave.ViewModels
{
    public class NetworkViewModel : INotifyPropertyChanged
    {
        private readonly NetworkService _networkService;
        private readonly StateService _stateTracker;
        private readonly SettingService _configManager;
        private readonly SynchronizationContext _syncContext;

        public LanguageService LanguageService { get; }
        public event Action<string> OnNewRemoteActivity;

        private ConnectionStatus _currentConnectionStatus = ConnectionStatus.Disconnected;

        private string _connectionStatusText;
        public string ConnectionStatusText { get => _connectionStatusText; set { _connectionStatusText = value; OnPropertyChanged(nameof(ConnectionStatusText)); } }

        private string _connectionStatusColor = "#E74C3C";
        public string ConnectionStatusColor { get => _connectionStatusColor; set { _connectionStatusColor = value; OnPropertyChanged(nameof(ConnectionStatusColor)); } }

        private ObservableCollection<RemoteClientState> _remoteStates;
        public ObservableCollection<RemoteClientState> RemoteStates { get => _remoteStates; set { _remoteStates = value; OnPropertyChanged(nameof(RemoteStates)); } }

        public ICommand RefreshProcessesCommand { get; }

        public NetworkViewModel(NetworkService networkService, StateService stateTracker, SettingService configManager, LanguageService languageService, SynchronizationContext syncContext)
        {
            _networkService = networkService;
            _stateTracker = stateTracker;
            _configManager = configManager;
            LanguageService = languageService;
            _syncContext = syncContext;
            RemoteStates = new ObservableCollection<RemoteClientState>();
            RefreshProcessesCommand = new RelayViewModel(ExecuteRefreshProcesses);
            _networkService.OnMessageReceived += HandleNetworkMessage;
            _networkService.OnConnectionStatusChanged += HandleConnectionStatusChanged;
            LanguageService.PropertyChanged += (s, e) => { if (e.PropertyName == "Item[]") UpdateConnectionStatusUI(); };
            ConnectionStatusText = LanguageService["StatusDisconnected"];
            StartAutoRefresh();
        }

        private void StartAutoRefresh() { Task.Run(async () => { while (true) { if (_currentConnectionStatus == ConnectionStatus.Connected) _networkService.SendMessage("[GET_STATES]"); await Task.Delay(2000); } }); }
        private void ExecuteRefreshProcesses(object parameter) { if (_currentConnectionStatus == ConnectionStatus.Connected) _networkService.SendMessage("[GET_STATES]"); }
        private void HandleConnectionStatusChanged(ConnectionStatus status) { _currentConnectionStatus = status; UpdateConnectionStatusUI(); if (status == ConnectionStatus.Connected) _stateTracker.BroadcastState(); }

        private void UpdateConnectionStatusUI()
        {
            Action action = () => { if (_currentConnectionStatus == ConnectionStatus.Connected) { ConnectionStatusText = LanguageService["StatusConnected"]; ConnectionStatusColor = "#2ECC71"; } else if (_currentConnectionStatus == ConnectionStatus.Connecting) { ConnectionStatusText = LanguageService["StatusConnecting"]; ConnectionStatusColor = "#F39C12"; } else { ConnectionStatusText = LanguageService["StatusDisconnected"]; ConnectionStatusColor = "#E74C3C"; } };
            if (_syncContext != null) _syncContext.Post(_ => action(), null);
            else action();
        }

        private void HandleNetworkMessage(string message)
        {
            if (message.StartsWith("[END]|") || message.StartsWith("[PROGRESS]|") || message.StartsWith("[START]|"))
            {
                var parts = message.Split('|');
                if (parts.Length >= 3)
                {
                    string rClientId = parts[1];
                    string rJobName = parts[2].Trim();

                    string uiMsg = "";
                    if (message.StartsWith("[START]")) uiMsg = $"[{rClientId}] {rJobName} : START";
                    if (message.StartsWith("[END]")) uiMsg = $"[{rClientId}] {rJobName} : END";
                    if (!string.IsNullOrEmpty(uiMsg)) OnNewRemoteActivity?.Invoke(uiMsg);

                    Action action = () =>
                    {
                        try
                        {
                            var client = RemoteStates.FirstOrDefault(c => c.ClientId == rClientId);
                            var job = client?.Jobs.FirstOrDefault(j => j.Name == rJobName);
                            if (job != null)
                            {
                                if (message.StartsWith("[END]")) { job.State = LanguageService["StateFinished"]; job.Progression = 100; job.NbFilesLeftToDo = 0; job.CurrentSpeed = ""; }
                                else if (message.StartsWith("[START]")) { job.State = LanguageService["StateActive"]; job.Progression = 0; job.CurrentSpeed = ""; }
                            }
                        }
                        catch { }
                    };
                    if (_syncContext != null) _syncContext.Post(_ => action(), null);
                    else action();
                }
            }
            else if (message.StartsWith("[STATES_RESPONSE]|"))
            {
                string jsonPayload = message.Substring(18);
                try
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonPayload);
                    if (dict == null) return;

                    var currentSettings = _configManager.LoadSettings();
                    var parsedClientStates = new Dictionary<string, List<ClientJobState>>();

                    foreach (var kvp in dict)
                    {
                        if (kvp.Key == currentSettings.ClientName) continue;
                        try
                        {
                            var parsedJobs = JsonSerializer.Deserialize<List<ClientJobState>>(kvp.Value.GetRawText());
                            if (parsedJobs != null)
                            {
                                foreach (var pj in parsedJobs)
                                {
                                    switch (pj.State)
                                    {
                                        case "ACTIVE": pj.State = LanguageService["StateActive"]; break;
                                        case "INACTIVE": pj.State = LanguageService["StateInactive"]; break;
                                        case "FINISHED":
                                        case "END": pj.State = LanguageService["StateFinished"]; break;
                                        case "BLOCKED": pj.State = LanguageService["StateBlocked"]; break;
                                        case "SUSPENDED": pj.State = LanguageService["StateSuspended"]; break;
                                        case "PAUSE_PENDING": pj.State = LanguageService["StatePausePending"]; break;
                                        case "WAITING": pj.State = LanguageService["StateWaiting"]; break;
                                    }

                                    // Traduction distante de la vitesse/chiffrement
                                    if (pj.CurrentSpeed == "ENCRYPTING")
                                    {
                                        pj.CurrentSpeed = LanguageService["StateEncrypting"];
                                    }
                                    else if (!string.IsNullOrEmpty(pj.CurrentSpeed) && pj.CurrentSpeed.Contains("Mo/s") && LanguageService.CurrentLanguage == "EN")
                                    {
                                        pj.CurrentSpeed = pj.CurrentSpeed.Replace("Mo/s", "MB/s");
                                    }
                                }
                                parsedClientStates[kvp.Key] = parsedJobs;
                            }
                        }
                        catch { }
                    }

                    if (_syncContext != null)
                    {
                        _syncContext.Post(_ =>
                        {
                            try
                            {
                                var activeIds = parsedClientStates.Keys.ToList();
                                var clientsToRemove = RemoteStates.Where(c => !activeIds.Contains(c.ClientId)).ToList();
                                foreach (var c in clientsToRemove) RemoteStates.Remove(c);

                                foreach (var kvp in parsedClientStates)
                                {
                                    var client = RemoteStates.FirstOrDefault(c => c.ClientId == kvp.Key);
                                    if (client == null)
                                    {
                                        client = new RemoteClientState { ClientId = kvp.Key, Jobs = new ObservableCollection<ClientJobState>() };
                                        RemoteStates.Add(client);
                                    }

                                    var currentJobNames = kvp.Value.Select(j => j.Name).ToList();
                                    var jobsToRemove = client.Jobs.Where(j => !currentJobNames.Contains(j.Name)).ToList();
                                    foreach (var j in jobsToRemove) client.Jobs.Remove(j);

                                    foreach (var pj in kvp.Value)
                                    {
                                        var existingJob = client.Jobs.FirstOrDefault(j => j.Name == pj.Name);
                                        if (existingJob == null)
                                        {
                                            client.Jobs.Add(pj);
                                        }
                                        else
                                        {
                                            existingJob.State = pj.State;
                                            existingJob.Progression = pj.Progression;
                                            existingJob.CurrentSpeed = pj.CurrentSpeed;
                                            existingJob.NbFilesLeftToDo = pj.NbFilesLeftToDo;
                                            existingJob.LastActionDate = pj.LastActionDate;
                                        }
                                    }
                                }
                            }
                            catch { }
                        }, null);
                    }
                    else
                    {
                        try
                        {
                            var newStates = new ObservableCollection<RemoteClientState>();
                            foreach (var kvp in parsedClientStates)
                            {
                                var newClient = new RemoteClientState { ClientId = kvp.Key, Jobs = new ObservableCollection<ClientJobState>() };
                                foreach (var pj in kvp.Value) newClient.Jobs.Add(pj);
                                newStates.Add(newClient);
                            }
                            RemoteStates = newStates;
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class ClientJobState : INotifyPropertyChanged
    {
        private string _name;
        [JsonPropertyName("Name")] public string Name { get => _name; set { _name = value; OnPropertyChanged(nameof(Name)); } }

        private string _state;
        [JsonPropertyName("State")] public string State { get => _state; set { _state = value; OnPropertyChanged(nameof(State)); } }

        private int _totalFilesToCopy;
        [JsonPropertyName("TotalFilesToCopy")] public int TotalFilesToCopy { get => _totalFilesToCopy; set { _totalFilesToCopy = value; OnPropertyChanged(nameof(TotalFilesToCopy)); } }

        private int _nbFilesLeftToDo;
        [JsonPropertyName("NbFilesLeftToDo")] public int NbFilesLeftToDo { get => _nbFilesLeftToDo; set { _nbFilesLeftToDo = value; OnPropertyChanged(nameof(NbFilesLeftToDo)); } }

        private int _progression;
        [JsonPropertyName("Progression")]
        public int Progression
        {
            get => _progression;
            set { _progression = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(nameof(Progression)); }
        }

        private string _currentSpeed;
        [JsonPropertyName("CurrentSpeed")] public string CurrentSpeed { get => _currentSpeed; set { _currentSpeed = value; OnPropertyChanged(nameof(CurrentSpeed)); } }

        private string _lastActionDate;
        [JsonPropertyName("LastActionDate")] public string LastActionDate { get => _lastActionDate; set { _lastActionDate = value; OnPropertyChanged(nameof(LastActionDate)); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class RemoteClientState : INotifyPropertyChanged
    {
        private string _clientId;
        public string ClientId { get => _clientId; set { _clientId = value; OnPropertyChanged(nameof(ClientId)); } }

        private ObservableCollection<ClientJobState> _jobs;
        public ObservableCollection<ClientJobState> Jobs { get => _jobs; set { _jobs = value; OnPropertyChanged(nameof(Jobs)); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
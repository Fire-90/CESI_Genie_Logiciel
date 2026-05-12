using EasySave.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace EasySave.ViewModels
{
    public class NetworkViewModel : INotifyPropertyChanged
    {
        private readonly NetworkService _networkService;
        private readonly StateTracker _stateTracker;
        private readonly ConfigManager _configManager;
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

        public NetworkViewModel(NetworkService networkService, StateTracker stateTracker, ConfigManager configManager, LanguageService languageService, SynchronizationContext syncContext)
        {
            _networkService = networkService;
            _stateTracker = stateTracker;
            _configManager = configManager;
            LanguageService = languageService;
            _syncContext = syncContext;
            RemoteStates = new ObservableCollection<RemoteClientState>();
            RefreshProcessesCommand = new RelayCommand(ExecuteRefreshProcesses);
            _networkService.OnMessageReceived += HandleNetworkMessage;
            _networkService.OnConnectionStatusChanged += HandleConnectionStatusChanged;
            LanguageService.PropertyChanged += (s, e) => { if (e.PropertyName == "Item[]") UpdateConnectionStatusUI(); };
            ConnectionStatusText = LanguageService["StatusDisconnected"];
            StartAutoRefresh();
        }

        private void StartAutoRefresh() { Task.Run(async () => { while (true) { if (_currentConnectionStatus == ConnectionStatus.Connected) _networkService.SendMessage("[GET_STATES]"); await Task.Delay(2000); } }); }
        private void ExecuteRefreshProcesses(object parameter) { if (_currentConnectionStatus == ConnectionStatus.Connected) _networkService.SendMessage("[GET_STATES]"); }
        private void HandleConnectionStatusChanged(ConnectionStatus status) { _currentConnectionStatus = status; UpdateConnectionStatusUI(); if (status == ConnectionStatus.Connected) _stateTracker.BroadcastState(); }

        private void RunOnUIThread(Action action)
        {
            if (_syncContext != null)
            {
                _syncContext.Post(_ => action(), null);
            }
            else
            {
                action();
            }
        }

        private void UpdateConnectionStatusUI() { RunOnUIThread(() => { if (_currentConnectionStatus == ConnectionStatus.Connected) { ConnectionStatusText = LanguageService["StatusConnected"]; ConnectionStatusColor = "#2ECC71"; } else if (_currentConnectionStatus == ConnectionStatus.Connecting) { ConnectionStatusText = LanguageService["StatusConnecting"]; ConnectionStatusColor = "#F39C12"; } else { ConnectionStatusText = LanguageService["StatusDisconnected"]; ConnectionStatusColor = "#E74C3C"; } }); }

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

                    RunOnUIThread(() =>
                    {
                        try
                        {
                            var client = RemoteStates.FirstOrDefault(c => c.ClientId == rClientId);
                            var job = client?.Jobs.FirstOrDefault(j => j.Name == rJobName);
                            if (job != null)
                            {
                                if (message.StartsWith("[END]")) { job.State = LanguageService["StateFinished"]; job.Progression = 100; job.NbFilesLeftToDo = 0; }
                                else if (message.StartsWith("[START]")) { job.State = LanguageService["StateActive"]; job.Progression = 0; }
                            }
                        }
                        catch { }
                    });
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
                                parsedClientStates[kvp.Key] = parsedJobs;
                            }
                        }
                        catch { }
                    }

                    RunOnUIThread(() =>
                    {
                        try
                        {
                            var activeIds = parsedClientStates.Keys.ToList();

                            var clientsToRemove = RemoteStates.Where(c => !activeIds.Contains(c.ClientId)).ToList();
                            foreach (var c in clientsToRemove)
                            {
                                RemoteStates.Remove(c);
                            }

                            foreach (var kvp in parsedClientStates)
                            {
                                var client = RemoteStates.FirstOrDefault(c => c.ClientId == kvp.Key);
                                if (client == null)
                                {
                                    client = new RemoteClientState { ClientId = kvp.Key, Jobs = new ObservableCollection<ClientJobState>() };
                                    RemoteStates.Add(client);
                                }

                                foreach (var pj in kvp.Value)
                                {
                                    var existingJob = client.Jobs.FirstOrDefault(j => j.Name == pj.Name);
                                    if (existingJob == null)
                                    {
                                        client.Jobs.Add(pj);
                                    }
                                    else if (existingJob.State != LanguageService["StateFinished"] || pj.State != "INACTIVE")
                                    {
                                        existingJob.State = pj.State;
                                        existingJob.Progression = pj.Progression;
                                        existingJob.NbFilesLeftToDo = pj.NbFilesLeftToDo;
                                        existingJob.LastActionDate = pj.LastActionDate;
                                    }
                                }
                            }
                        }
                        catch { }
                    });
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
        [JsonPropertyName("Progression")] public int Progression { get => _progression; set { _progression = value; OnPropertyChanged(nameof(Progression)); } }
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
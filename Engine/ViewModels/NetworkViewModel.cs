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

        private ConnectionStatus _currentConnectionStatus = ConnectionStatus.Disconnected;

        private string _connectionStatusText;
        public string ConnectionStatusText
        {
            get => _connectionStatusText;
            set { _connectionStatusText = value; OnPropertyChanged(nameof(ConnectionStatusText)); }
        }

        private string _connectionStatusColor = "#E74C3C";
        public string ConnectionStatusColor
        {
            get => _connectionStatusColor;
            set { _connectionStatusColor = value; OnPropertyChanged(nameof(ConnectionStatusColor)); }
        }

        private ObservableCollection<RemoteClientState> _remoteStates;
        public ObservableCollection<RemoteClientState> RemoteStates
        {
            get => _remoteStates;
            set { _remoteStates = value; OnPropertyChanged(nameof(RemoteStates)); }
        }

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

            // Mise à jour de la langue dynamiquement
            LanguageService.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == null || e.PropertyName == "Item[]")
                    UpdateConnectionStatusUI();
            };

            ConnectionStatusText = LanguageService["StatusDisconnected"];
            StartAutoRefresh();
        }

        private void StartAutoRefresh()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    if (_currentConnectionStatus == ConnectionStatus.Connected)
                    {
                        _networkService.SendMessage("[GET_STATES]");
                    }
                    await Task.Delay(2000);
                }
            });
        }

        private void ExecuteRefreshProcesses(object parameter)
        {
            if (_currentConnectionStatus == ConnectionStatus.Connected)
            {
                _networkService.SendMessage("[GET_STATES]");
            }
        }

        private void HandleConnectionStatusChanged(ConnectionStatus status)
        {
            _currentConnectionStatus = status;
            UpdateConnectionStatusUI();

            if (status == ConnectionStatus.Connected)
            {
                _stateTracker.BroadcastState();
            }
        }

        private void UpdateConnectionStatusUI()
        {
            Action updateUiAction = () =>
            {
                if (_currentConnectionStatus == ConnectionStatus.Connected)
                {
                    ConnectionStatusText = LanguageService["StatusConnected"];
                    ConnectionStatusColor = "#2ECC71";
                }
                else if (_currentConnectionStatus == ConnectionStatus.Connecting)
                {
                    ConnectionStatusText = LanguageService["StatusConnecting"];
                    ConnectionStatusColor = "#F39C12";
                }
                else
                {
                    ConnectionStatusText = LanguageService["StatusDisconnected"];
                    ConnectionStatusColor = "#E74C3C";
                }
            };

            if (_syncContext != null) _syncContext.Post(_ => updateUiAction(), null);
            else updateUiAction();
        }

        private void HandleNetworkMessage(string message)
        {
            if (message.StartsWith("[END]|"))
            {
                var parts = message.Split(new[] { '|' }, 3);
                if (parts.Length == 3)
                {
                    string rClientId = parts[1];
                    string rJobName = parts[2].Trim();

                    Action showSuccessAction = () =>
                    {
                        var clientState = RemoteStates.FirstOrDefault(c => c.ClientId == rClientId);
                        var job = clientState?.Jobs.FirstOrDefault(j => j.Name == rJobName);
                        if (job != null)
                        {
                            job.State = LanguageService["StateFinished"];
                            job.Progression = 100;
                            job.NbFilesLeftToDo = 0;
                            job.LastActionDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");

                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(3000);
                                Action resetAction = () =>
                                {
                                    if (job.State == "Save terminée" || job.State == "Finished" || job.State == LanguageService["StateFinished"])
                                    {
                                        job.State = "INACTIVE";
                                        job.Progression = 0;
                                    }
                                };
                                if (_syncContext != null) _syncContext.Post(_ => resetAction(), null);
                                else resetAction();
                            });
                        }
                    };
                    if (_syncContext != null) _syncContext.Post(_ => showSuccessAction(), null);
                    else showSuccessAction();
                }
            }
            else if (message.StartsWith("[STATES_RESPONSE]|"))
            {
                string jsonPayload = message.Substring(18);
                try
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonPayload);

                    Action updateUiAction = () =>
                    {
                        if (dict != null)
                        {
                            var activeClientIds = dict.Keys.ToList();
                            var clientsToRemove = RemoteStates.Where(c => !activeClientIds.Contains(c.ClientId)).ToList();
                            foreach (var c in clientsToRemove) RemoteStates.Remove(c);

                            var currentSettings = _configManager.LoadSettings();
                            foreach (var kvp in dict)
                            {
                                if (kvp.Key == currentSettings.ClientName) continue;

                                var clientState = RemoteStates.FirstOrDefault(c => c.ClientId == kvp.Key);
                                if (clientState == null)
                                {
                                    clientState = new RemoteClientState { ClientId = kvp.Key, Jobs = new ObservableCollection<ClientJobState>() };
                                    RemoteStates.Add(clientState);
                                }

                                try
                                {
                                    var parsedJobs = JsonSerializer.Deserialize<List<ClientJobState>>(kvp.Value.GetRawText());
                                    if (parsedJobs != null)
                                    {
                                        foreach (var pj in parsedJobs)
                                        {
                                            var existingJob = clientState.Jobs.FirstOrDefault(j => j.Name == pj.Name);
                                            if (existingJob == null) clientState.Jobs.Add(pj);
                                            else
                                            {
                                                if ((existingJob.State == "Save terminée" || existingJob.State == "Finished" || existingJob.State == LanguageService["StateFinished"]) && pj.State == "INACTIVE")
                                                    continue;

                                                existingJob.State = pj.State;
                                                existingJob.Progression = pj.Progression;
                                                existingJob.NbFilesLeftToDo = pj.NbFilesLeftToDo;
                                                existingJob.TotalFilesToCopy = pj.TotalFilesToCopy;
                                                existingJob.LastActionDate = pj.LastActionDate;
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    };

                    if (_syncContext != null) _syncContext.Post(_ => updateUiAction(), null);
                    else updateUiAction();
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

        private double _progression;
        [JsonPropertyName("Progression")] public double Progression { get => _progression; set { _progression = value; OnPropertyChanged(nameof(Progression)); } }

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
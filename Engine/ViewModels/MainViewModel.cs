using EasySave.Models;
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
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ConfigManager _configManager;
        private readonly BackupEngine _backupEngine;
        private readonly StateTracker _stateTracker;
        private readonly NetworkService _networkService;
        private readonly SynchronizationContext _syncContext;
        private readonly SynchronizationContext _uiContext;

        public LanguageService LanguageService { get; } = new LanguageService();

        public ObservableCollection<JobViewModel> Jobs { get; }
        public List<BackupType> AvailableTypes { get; } = new List<BackupType> { BackupType.Full, BackupType.Differential };
        public bool IsJobSelected => SelectedJob != null;

        private JobViewModel _selectedJob;
        public JobViewModel SelectedJob
        {
            get => _selectedJob;
            set
            {
                if (_selectedJob != value)
                {
                    _selectedJob = value;
                    OnPropertyChanged(nameof(SelectedJob));
                    OnPropertyChanged(nameof(IsJobSelected));
                    (ExecuteSelectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (DeleteJobCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private string _currentFile;
        public string CurrentFile
        {
            get => _currentFile;
            private set
            {
                if (_currentFile != value)
                {
                    _currentFile = value;
                    OnPropertyChanged(nameof(CurrentFile));
                }
            }
        }

        public AppSettings CurrentSettings { get; private set; }
        public ObservableCollection<string> Softwares { get; set; }

        public List<string> AvailableLogFormats { get; } = new List<string> { "JSON", "XML" };
        public List<string> AvailableLogDestinations { get; } = new List<string> { "LocalOnly", "ServerOnly", "LocalAndServer" };

        public string SelectedLogFormat
        {
            get => CurrentSettings?.LogFormat?.ToUpper() ?? "JSON";
            set
            {
                string newValue = value?.ToLower() ?? "json";
                if (CurrentSettings != null && CurrentSettings.LogFormat != newValue)
                {
                    CurrentSettings.LogFormat = newValue;
                    OnPropertyChanged(nameof(SelectedLogFormat));
                    SaveConfig();
                }
            }
        }

        public string SelectedLogDestination
        {
            get => CurrentSettings?.LogDestination ?? "LocalAndServer";
            set
            {
                if (CurrentSettings != null && CurrentSettings.LogDestination != value)
                {
                    CurrentSettings.LogDestination = value;
                    OnPropertyChanged(nameof(SelectedLogDestination));
                    SaveConfig();
                }
            }
        }

        public string ServerIP
        {
            get => CurrentSettings?.ServerIP ?? "127.0.0.1";
            set
            {
                if (CurrentSettings != null && CurrentSettings.ServerIP != value)
                {
                    CurrentSettings.ServerIP = value;
                    OnPropertyChanged(nameof(ServerIP));
                    SaveConfig();
                }
            }
        }

        public string ClientName
        {
            get => CurrentSettings?.ClientName ?? "EasySaveClient";
            set
            {
                if (CurrentSettings != null && CurrentSettings.ClientName != value)
                {
                    CurrentSettings.ClientName = value;
                    OnPropertyChanged(nameof(ClientName));
                    SaveConfig();
                }
            }
        }

        public string EncryptedExtensionsString
        {
            get => CurrentSettings?.EncryptedExtensions == null ? "" : string.Join(";", CurrentSettings.EncryptedExtensions);
            set
            {
                if (CurrentSettings != null)
                {
                    var extensions = value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim()).ToList();
                    CurrentSettings.EncryptedExtensions = extensions;
                    OnPropertyChanged(nameof(EncryptedExtensionsString));
                    SaveConfig();
                }
            }
        }

        public string PriorityExtensionsString
        {
            get => CurrentSettings?.PriorityExtensions == null ? "" : string.Join(";", CurrentSettings.PriorityExtensions);
            set
            {
                if (CurrentSettings != null)
                {
                    var extensions = value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim()).ToList();
                    CurrentSettings.PriorityExtensions = extensions;
                    OnPropertyChanged(nameof(PriorityExtensionsString));
                }
            }
        }

        private bool _isSettingsOpen = false;
        public bool IsSettingsOpen
        {
            get => _isSettingsOpen;
            set { _isSettingsOpen = value; OnPropertyChanged(nameof(IsSettingsOpen)); }
        }

        private string _newSoftware;
        public string NewSoftware
        {
            get => _newSoftware;
            set { _newSoftware = value; OnPropertyChanged(nameof(NewSoftware)); }
        }

        public string SelectedSoftware { get; set; }

        // --- STATUT DU RÉSEAU ---
        private ConnectionStatus _currentConnectionStatus = ConnectionStatus.Disconnected;

        private string _connectionStatusText = "Échec / Déconnecté";
        public string ConnectionStatusText
        {
            get => _connectionStatusText;
            set { _connectionStatusText = value; OnPropertyChanged(nameof(ConnectionStatusText)); }
        }

        private string _connectionStatusColor = "#E74C3C"; // Rouge
        public string ConnectionStatusColor
        {
            get => _connectionStatusColor;
            set { _connectionStatusColor = value; OnPropertyChanged(nameof(ConnectionStatusColor)); }
        }

        // --- PROPRIÉTÉ POUR LE RÉSEAU (CLIENTS DISTANTS UNIQUEMENT) ---
        private ObservableCollection<RemoteClientState> _remoteStates;
        public ObservableCollection<RemoteClientState> RemoteStates
        {
            get => _remoteStates;
            set { _remoteStates = value; OnPropertyChanged(nameof(RemoteStates)); }
        }

        public ICommand AddJobCommand { get; }
        public ICommand ExecuteSelectionCommand { get; }
        public ICommand DeleteJobCommand { get; }
        public ICommand ChangeLanguageCommand { get; }
        public ICommand AddSoftwareCommand { get; }
        public ICommand RemoveSoftwareCommand { get; }
        public ICommand RefreshProcessesCommand { get; }
        public ICommand ToggleSettingsCommand { get; }

        // Team feature (Pause / Resume / Stop jobs via the DataGrid buttons)
        public ICommand PauseJobCommand { get; }
        public ICommand ResumeJobCommand { get; }
        public ICommand StopJobCommand { get; }

        public MainViewModel(ConfigManager configManager, StateTracker stateTracker, BackupEngine backupEngine, NetworkService networkService)
        {
            _configManager = configManager;
            _stateTracker = stateTracker;
            _backupEngine = backupEngine;
            _networkService = networkService;

            _uiContext = SynchronizationContext.Current;
            _syncContext = SynchronizationContext.Current;

            CurrentSettings = _configManager.LoadSettings();
            Jobs = new ObservableCollection<JobViewModel>(CurrentSettings.Jobs.Select(j => new JobViewModel(j)));

            foreach (var job in Jobs) job.PropertyChanged += OnJobPropertyChanged;

            Softwares = new ObservableCollection<string>(CurrentSettings.BusinessSoftwares);
            RemoteStates = new ObservableCollection<RemoteClientState>();

            _backupEngine.OnProgressUpdate += (file, remaining) =>
            {
                CurrentFile = file;
                _networkService.SendMessage($"[PROGRESS] {file}");
            };

            EasyLog.DailyLogger.OnLogGenerated += (jobId, format, entry) =>
            {
                try
                {
                    string jsonLog = System.Text.Json.JsonSerializer.Serialize(entry);
                    _networkService.SendMessage($"[LOG]|{jobId}|{format}|{jsonLog}");
                }
                catch { }
            };

            EasySave.Services.StateTracker.OnStateUpdated += (jsonState) =>
            {
                _networkService.SendMessage($"[STATE]|{jsonState}");
            };

            _networkService.OnMessageReceived += HandleNetworkMessage;
            _networkService.OnConnectionStatusChanged += HandleConnectionStatusChanged;

            _backupEngine.OnJobProgress += (jobName, progress) =>
            {
                _uiContext?.Post(_ =>
                {
                    var jobVm = Jobs.FirstOrDefault(j => j.Name == jobName);
                    if (jobVm != null) jobVm.Progress = progress;
                }, null);
            };

            AddJobCommand = new RelayCommand(ExecuteAddJob);
            ExecuteSelectionCommand = new RelayCommand(ExecuteSelectedJob);
            DeleteJobCommand = new RelayCommand(ExecuteDeleteJob, CanExecuteSelectedJob);
            ChangeLanguageCommand = new RelayCommand(lang => ChangeLanguage(lang));
            ToggleSettingsCommand = new RelayCommand(p => IsSettingsOpen = !IsSettingsOpen);
            AddSoftwareCommand = new RelayCommand(ExecuteAddSoftware);
            RemoveSoftwareCommand = new RelayCommand(ExecuteRemoveSoftware);
            RefreshProcessesCommand = new RelayCommand(ExecuteRefreshProcesses);

            // Job controls
            PauseJobCommand = new RelayCommand(ExecutePauseJob);
            ResumeJobCommand = new RelayCommand(ExecuteResumeJob);
            StopJobCommand = new RelayCommand(ExecuteStopJob);

            this.LanguageService.CurrentLanguage = CurrentSettings.Language;
            ExecuteRefreshProcesses(null);
            ChangeLanguage(CurrentSettings.Language);

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
                bool isEn = CurrentSettings.Language == "EN";

                if (_currentConnectionStatus == ConnectionStatus.Connected)
                {
                    ConnectionStatusText = isEn ? "Connected" : "Connecté";
                    ConnectionStatusColor = "#2ECC71"; // Vert
                }
                else if (_currentConnectionStatus == ConnectionStatus.Connecting)
                {
                    ConnectionStatusText = isEn ? "Connecting..." : "En cours...";
                    ConnectionStatusColor = "#F39C12"; // Orange
                }
                else
                {
                    ConnectionStatusText = isEn ? "Failed / Disconnected" : "Échec / Déconnecté";
                    ConnectionStatusColor = "#E74C3C"; // Rouge
                }
            };

            if (_syncContext != null) _syncContext.Post(_ => updateUiAction(), null);
            else updateUiAction();
        }

        private void HandleNetworkMessage(string message)
        {
            // RECEPTION DU MESSAGE DE FIN DU SERVEUR
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
                            job.State = LanguageService.CurrentLanguage == "EN" ? "Finished" : "Save terminée";
                            job.Progression = 100;
                            job.NbFilesLeftToDo = 0;
                            job.LastActionDate = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");

                            // Temporisation de 3 secondes avant de remettre l'affichage en INACTIVE
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(3000);
                                Action resetAction = () =>
                                {
                                    if (job.State == "Save terminée" || job.State == "Finished")
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
            // LECTURE DE LA BASE DES ETATS (Actualisation intelligente)
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

                            // 1. On retire les clients déconnectés
                            var clientsToRemove = RemoteStates.Where(c => !activeClientIds.Contains(c.ClientId)).ToList();
                            foreach (var c in clientsToRemove) RemoteStates.Remove(c);

                            // 2. On met à jour les clients connectés (sans écraser l'animation de succès)
                            foreach (var kvp in dict)
                            {
                                if (kvp.Key == CurrentSettings.ClientName) continue;

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
                                            if (existingJob == null)
                                            {
                                                clientState.Jobs.Add(pj);
                                            }
                                            else
                                            {
                                                // IMPORTANT : Si la carte est en train d'afficher "Save terminée", on ne l'écrase pas avec INACTIVE.
                                                if ((existingJob.State == "Save terminée" || existingJob.State == "Finished") && pj.State == "INACTIVE")
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
                catch (Exception ex)
                {
                    Action showErrorAction = () => CurrentFile = $"{this.LanguageService["MsgError"]} {ex.Message}";
                    if (_syncContext != null) _syncContext.Post(_ => showErrorAction(), null);
                    else showErrorAction();
                }
            }
        }

        private void OnJobPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Progress" || e.PropertyName == "IsSelected") return;
            SaveConfig();
        }

        private void ChangeLanguage(object param)
        {
            string lang = param as string ?? "EN";
            CurrentSettings.Language = lang;
            CurrentSettings.Jobs = Jobs.Select(j => j.Model).ToList();
            _configManager.SaveSettings(CurrentSettings);
            this.LanguageService.CurrentLanguage = lang;
        }

        private void SaveSettings(object parameter)
        {
            CurrentSettings.BusinessSoftwares = Softwares.ToList();
            CurrentSettings.Jobs = Jobs.Select(j => j.Model).ToList();
            _configManager.SaveSettings(CurrentSettings);
            IsSettingsOpen = false;
            SaveConfig();
            UpdateConnectionStatusUI();
        }

        private void ExecuteAddJob(object parameter)
        {
            int newId = Jobs.Count > 0 ? Jobs.Max(j => j.Id) + 1 : 1;
            var newModel = new BackupJob { Id = newId, Name = $"Save {newId}", SourceDirectory = "", TargetDirectory = "", Type = BackupType.Full };
            var newViewModel = new JobViewModel(newModel);
            newViewModel.PropertyChanged += OnJobPropertyChanged;
            Jobs.Add(newViewModel);
            SaveConfig();
            SelectedJob = newViewModel;
            CurrentFile = this.LanguageService["MsgSlotAdded"];
        }

        private void ExecuteDeleteJob(object parameter)
        {
            if (SelectedJob != null)
            {
                SelectedJob.PropertyChanged -= OnJobPropertyChanged;
                Jobs.Remove(SelectedJob);
                SaveConfig();
                CurrentFile = this.LanguageService["MsgDeleted"];
                SelectedJob = null;
            }
        }

        private bool CanExecuteSelectedJob(object parameter) => SelectedJob != null;

        private async void ExecuteSelectedJob(object parameter)
        {
            var jobsToRun = Jobs.Where(j => j.IsSelected).ToList();
            if (!jobsToRun.Any())
            {
                CurrentFile = this.LanguageService["MsgEmptyPath"];
                return;
            }

            try
            {
                CurrentFile = this.LanguageService["MsgStartGlobal"];
                var tasks = new List<Task>();

                foreach (var jobVm in jobsToRun)
                {
                    if (string.IsNullOrWhiteSpace(jobVm.SourceDirectory) || string.IsNullOrWhiteSpace(jobVm.TargetDirectory)) continue;

                    jobVm.Progress = 0;
                    jobVm.IsRunning = true;
                    jobVm.IsPaused = false;
                    _networkService.SendMessage($"[START] {jobVm.Name}");

                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await _backupEngine.ExecuteJobAsync(jobVm.Model);
                            _networkService.SendMessage($"[END] {jobVm.Name} - Success");
                        }
                        catch (Exception ex)
                        {
                            string errorMsg = (ex is InvalidOperationException && ex.Message.StartsWith("BLOCKING|"))
                                ? $"{this.LanguageService["MsgBlockingSoftware"]} {ex.Message.Split('|')[1]}"
                                : $"{this.LanguageService["MsgError"]} {ex.Message}";

                            _uiContext?.Post(_ => CurrentFile = errorMsg, null);
                            _networkService.SendMessage($"[ERROR] {jobVm.Name} : {ex.Message}");
                        }
                        finally
                        {
                            jobVm.IsRunning = false;
                            jobVm.IsPaused = false;
                        }
                    }));
                }

                await Task.WhenAll(tasks);

                if (!CurrentFile.Contains("❌"))
                {
                    CurrentFile = this.LanguageService["MsgSuccessGlobal"];
                }
            }
            catch (Exception ex)
            {
                CurrentFile = $"{this.LanguageService["MsgError"]} {ex.Message}";
                _networkService.SendMessage($"[ERROR] Global error: {ex.Message}");
            }
        }

        public async Task ExecuteJobsAsync(List<int> ids)
        {
            try
            {
                CurrentFile = this.LanguageService["MsgStartGlobal"];
                var tasks = new List<Task>();

                foreach (var id in ids)
                {
                    var jobVm = Jobs.FirstOrDefault(j => j.Id == id);
                    if (jobVm == null || string.IsNullOrWhiteSpace(jobVm.SourceDirectory) || string.IsNullOrWhiteSpace(jobVm.TargetDirectory)) continue;

                    jobVm.Progress = 0;
                    jobVm.IsRunning = true;
                    jobVm.IsPaused = false;
                    _networkService.SendMessage($"[START] {jobVm.Name}");

                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await _backupEngine.ExecuteJobAsync(jobVm.Model);
                            _networkService.SendMessage($"[END] {jobVm.Name} - Success");
                        }
                        catch (Exception ex)
                        {
                            string errorMsg = (ex is InvalidOperationException && ex.Message.StartsWith("BLOCKING|"))
                                ? $"{this.LanguageService["MsgBlockingSoftware"]} {ex.Message.Split('|')[1]}"
                                : $"{this.LanguageService["MsgError"]} {ex.Message}";

                            _uiContext?.Post(_ => CurrentFile = errorMsg, null);
                            _networkService.SendMessage($"[ERROR] {jobVm.Name} : {ex.Message}");
                        }
                        finally
                        {
                            jobVm.IsRunning = false;
                            jobVm.IsPaused = false;
                        }
                    }));
                }

                await Task.WhenAll(tasks);

                if (!CurrentFile.Contains("❌"))
                {
                    CurrentFile = this.LanguageService["MsgSuccessGlobal"];
                }
            }
            catch (Exception ex)
            {
                CurrentFile = $"{this.LanguageService["MsgError"]} {ex.Message}";
                _networkService.SendMessage($"[ERROR] Global error: {ex.Message}");
            }
        }

        // Job Control Logics
        private void ExecutePauseJob(object parameter)
        {
            if (parameter is JobViewModel job)
            {
                _backupEngine.PauseJob(job.Name);
                job.IsPaused = true;
            }
        }

        private void ExecuteResumeJob(object parameter)
        {
            if (parameter is JobViewModel job)
            {
                _backupEngine.ResumeJob(job.Name);
                job.IsPaused = false;
            }
        }

        private void ExecuteStopJob(object parameter)
        {
            if (parameter is JobViewModel job)
            {
                _backupEngine.StopJob(job.Name);
                job.IsRunning = false;
                job.IsPaused = false;
            }
        }

        public void SaveConfig()
        {
            CurrentSettings.Jobs = Jobs.Select(j => j.Model).ToList();
            _configManager.SaveSettings(CurrentSettings);
        }

        private void ExecuteAddSoftware(object parameter)
        {
            if (!string.IsNullOrWhiteSpace(NewSoftware) && !Softwares.Contains(NewSoftware))
            {
                Softwares.Add(NewSoftware);
                CurrentSettings.BusinessSoftwares = Softwares.ToList();
                NewSoftware = "";
                SaveConfig();
            }
        }

        private void ExecuteRemoveSoftware(object parameter)
        {
            if (SelectedSoftware != null) Softwares.Remove(SelectedSoftware);
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

        private long _totalFilesSize;
        [JsonPropertyName("TotalFilesSize")] public long TotalFilesSize { get => _totalFilesSize; set { _totalFilesSize = value; OnPropertyChanged(nameof(TotalFilesSize)); } }

        private int _nbFilesLeftToDo;
        [JsonPropertyName("NbFilesLeftToDo")] public int NbFilesLeftToDo { get => _nbFilesLeftToDo; set { _nbFilesLeftToDo = value; OnPropertyChanged(nameof(NbFilesLeftToDo)); } }

        private double _progression;
        [JsonPropertyName("Progression")] public double Progression { get => _progression; set { _progression = value; OnPropertyChanged(nameof(Progression)); } }

        private string _lastActionDate;
        [JsonPropertyName("LastActionDate")] public string LastActionDate { get => _lastActionDate; set { _lastActionDate = value; OnPropertyChanged(nameof(LastActionDate)); } }

        private long _remainingFilesSize;
        [JsonPropertyName("RemainingFilesSize")] public long RemainingFilesSize { get => _remainingFilesSize; set { _remainingFilesSize = value; OnPropertyChanged(nameof(RemainingFilesSize)); } }

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
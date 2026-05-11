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

        // --- PROPRIÉTÉS DES TRAVAUX ---
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

        // --- PROPRIÉTÉS DES PARAMÈTRES (AUTO-SAVE) ---
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

        // NOUVEAUX CHAMPS
        public long MaxParallelFileSizeLimitKb
        {
            get => CurrentSettings?.MaxParallelFileSizeLimitKb ?? 50000;
            set
            {
                if (CurrentSettings != null && CurrentSettings.MaxParallelFileSizeLimitKb != value)
                {
                    CurrentSettings.MaxParallelFileSizeLimitKb = value;
                    OnPropertyChanged(nameof(MaxParallelFileSizeLimitKb));
                    SaveConfig();
                }
            }
        }

        public string EncryptionKey
        {
            get => CurrentSettings?.EncryptionKey ?? "EasySaveKey";
            set
            {
                if (CurrentSettings != null && CurrentSettings.EncryptionKey != value)
                {
                    CurrentSettings.EncryptionKey = value;
                    OnPropertyChanged(nameof(EncryptionKey));
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
                    SaveConfig();
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

        // --- ÉTATS DISTANTS (SERVEUR) ---
        private ObservableCollection<RemoteClientState> _remoteStates;
        public ObservableCollection<RemoteClientState> RemoteStates
        {
            get => _remoteStates;
            set { _remoteStates = value; OnPropertyChanged(nameof(RemoteStates)); }
        }

        // --- COMMANDES ---
        public ICommand AddJobCommand { get; }
        public ICommand ExecuteSelectionCommand { get; }
        public ICommand DeleteJobCommand { get; }
        public ICommand ChangeLanguageCommand { get; }
        public ICommand AddSoftwareCommand { get; }
        public ICommand RemoveSoftwareCommand { get; }
        public ICommand RefreshProcessesCommand { get; }
        public ICommand ToggleSettingsCommand { get; }
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

            // Événements Backup
            _backupEngine.OnProgressUpdate += (file, remaining) =>
            {
                if (_uiContext != null)
                {
                    _uiContext.Post(_ => CurrentFile = file, null);
                }
                else
                {
                    CurrentFile = file;
                }

                _networkService.SendMessage($"[PROGRESS] {file}");
            };

            _backupEngine.OnJobWaiting += (jobName, isWaiting) =>
            {
                Action updateWait = () =>
                {
                    var jobVm = Jobs.FirstOrDefault(j => j.Name == jobName);
                    if (jobVm != null) jobVm.IsWaiting = isWaiting;
                };

                if (_uiContext != null) _uiContext.Post(_ => updateWait(), null);
                else updateWait();
            };

            _backupEngine.OnJobProgress += (jobName, progress) =>
            {
                Action updateProgress = () =>
                {
                    var jobVm = Jobs.FirstOrDefault(j => j.Name == jobName);
                    if (jobVm != null)
                    {
                        jobVm.Progress = progress;
                        if (progress >= 100)
                        {
                            Task.Run(async () =>
                            {
                                await Task.Delay(1500);
                                if (_uiContext != null) _uiContext.Post(_ => jobVm.Progress = 0, null);
                                else jobVm.Progress = 0;
                            });
                        }
                    }
                };

                if (_uiContext != null) _uiContext.Post(_ => updateProgress(), null);
                else updateProgress();
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

            // Initialisation Commandes
            AddJobCommand = new RelayCommand(ExecuteAddJob);
            ExecuteSelectionCommand = new RelayCommand(ExecuteSelectedJob);
            DeleteJobCommand = new RelayCommand(ExecuteDeleteJob, CanExecuteSelectedJob);
            ChangeLanguageCommand = new RelayCommand(lang => ChangeLanguage(lang));
            ToggleSettingsCommand = new RelayCommand(p => IsSettingsOpen = !IsSettingsOpen);
            AddSoftwareCommand = new RelayCommand(ExecuteAddSoftware);
            RemoveSoftwareCommand = new RelayCommand(ExecuteRemoveSoftware);
            RefreshProcessesCommand = new RelayCommand(ExecuteRefreshProcesses);

            PauseJobCommand = new RelayCommand(ExecutePauseJob);
            ResumeJobCommand = new RelayCommand(ExecuteResumeJob);
            StopJobCommand = new RelayCommand(ExecuteStopJob);

            this.LanguageService.CurrentLanguage = CurrentSettings.Language;
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
            if (e.PropertyName == "Progress" || e.PropertyName == "IsSelected" ||
                e.PropertyName == "IsRunning" || e.PropertyName == "IsPaused" || e.PropertyName == "IsWaiting") return;

            SaveConfig();
        }

        private void ChangeLanguage(object param)
        {
            string lang = param as string ?? "EN";
            CurrentSettings.Language = lang;
            CurrentSettings.Jobs = Jobs.Select(j => j.Model).ToList();
            _configManager.SaveSettings(CurrentSettings);
            this.LanguageService.CurrentLanguage = lang;
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
                    jobVm.IsWaiting = false;

                    var propertyInfo = jobVm.GetType().GetProperty("IsPaused");
                    if (propertyInfo != null && propertyInfo.CanWrite)
                    {
                        propertyInfo.SetValue(jobVm, false);
                    }

                    _networkService.SendMessage($"[START] {jobVm.Name}");

                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await _backupEngine.ExecuteJobAsync(jobVm.Model);
                            _networkService.SendMessage($"[END] {jobVm.Name}");
                        }
                        catch (Exception ex)
                        {
                            if (ex.Message == "Job stopped manually.")
                            {
                                if (_uiContext != null) _uiContext.Post(_ => CurrentFile = LanguageService["MsgJobStopped"], null);
                                _networkService.SendMessage($"[STOPPED] {jobVm.Name}");
                            }
                            else
                            {
                                if (_uiContext != null) _uiContext.Post(_ => CurrentFile = $"{this.LanguageService["MsgError"]} {ex.Message}", null);
                                _networkService.SendMessage($"[ERROR] {jobVm.Name} : {ex.Message}");
                            }
                        }
                        finally
                        {
                            Action resetAction = () =>
                            {
                                jobVm.IsRunning = false;
                                jobVm.IsWaiting = false;
                                if (jobVm.Progress < 100) jobVm.Progress = 0;
                            };

                            if (_uiContext != null) _uiContext.Post(_ => resetAction(), null);
                            else resetAction();
                        }
                    }));
                }
                await Task.WhenAll(tasks);
                if (!CurrentFile.Contains("❌") && !CurrentFile.Contains("interrompue") && !CurrentFile.Contains("stopped"))
                {
                    CurrentFile = this.LanguageService["MsgSuccessGlobal"];
                }
            }
            catch (Exception ex)
            {
                CurrentFile = $"{this.LanguageService["MsgError"]} {ex.Message}";
            }
        }

        public async Task ExecuteJobsAsync(List<int> ids)
        {
            try
            {
                CurrentFile = this.LanguageService["MsgStartGlobal"];
                foreach (var id in ids)
                {
                    var jobVm = Jobs.FirstOrDefault(j => j.Id == id);
                    if (jobVm == null || string.IsNullOrWhiteSpace(jobVm.SourceDirectory) || string.IsNullOrWhiteSpace(jobVm.TargetDirectory)) continue;

                    jobVm.Progress = 0;
                    jobVm.IsRunning = true;
                    jobVm.IsWaiting = false;

                    var propertyInfo = jobVm.GetType().GetProperty("IsPaused");
                    if (propertyInfo != null && propertyInfo.CanWrite)
                    {
                        propertyInfo.SetValue(jobVm, false);
                    }

                    _networkService.SendMessage($"[START] {jobVm.Name}");
                    try
                    {
                        await _backupEngine.ExecuteJobAsync(jobVm.Model);
                        _networkService.SendMessage($"[END] {jobVm.Name}");
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message == "Job stopped manually.")
                        {
                            if (_uiContext != null) _uiContext.Post(_ => CurrentFile = LanguageService["MsgJobStopped"], null);
                            _networkService.SendMessage($"[STOPPED] {jobVm.Name}");
                        }
                        else
                        {
                            if (_uiContext != null) _uiContext.Post(_ => CurrentFile = $"{this.LanguageService["MsgError"]} {ex.Message}", null);
                            _networkService.SendMessage($"[ERROR] {jobVm.Name} : {ex.Message}");
                        }
                    }
                    finally
                    {
                        Action resetAction = () =>
                        {
                            jobVm.IsRunning = false;
                            jobVm.IsWaiting = false;
                            if (jobVm.Progress < 100) jobVm.Progress = 0;
                        };

                        if (_uiContext != null) _uiContext.Post(_ => resetAction(), null);
                        else resetAction();
                    }
                }

                if (!CurrentFile.Contains("❌") && !CurrentFile.Contains("interrompue") && !CurrentFile.Contains("stopped"))
                {
                    CurrentFile = this.LanguageService["MsgSuccessGlobal"];
                }
            }
            catch (Exception ex)
            {
                CurrentFile = $"{this.LanguageService["MsgError"]} {ex.Message}";
            }
        }

        private void ExecutePauseJob(object parameter)
        {
            if (parameter is JobViewModel job)
            {
                _backupEngine.PauseJob(job.Name);

                var propertyInfo = job.GetType().GetProperty("IsPaused");
                if (propertyInfo != null && propertyInfo.CanWrite)
                {
                    propertyInfo.SetValue(job, true);
                }
            }
        }

        private void ExecuteResumeJob(object parameter)
        {
            if (parameter is JobViewModel job)
            {
                _backupEngine.ResumeJob(job.Name);

                var propertyInfo = job.GetType().GetProperty("IsPaused");
                if (propertyInfo != null && propertyInfo.CanWrite)
                {
                    propertyInfo.SetValue(job, false);
                }
            }
        }

        private void ExecuteStopJob(object parameter)
        {
            if (parameter is JobViewModel job)
            {
                _backupEngine.StopJob(job.Name);
                job.IsRunning = false;
                job.IsWaiting = false;

                var propertyInfo = job.GetType().GetProperty("IsPaused");
                if (propertyInfo != null && propertyInfo.CanWrite)
                {
                    propertyInfo.SetValue(job, false);
                }
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
            if (SelectedSoftware != null)
            {
                Softwares.Remove(SelectedSoftware);
                CurrentSettings.BusinessSoftwares = Softwares.ToList();
                SaveConfig();
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
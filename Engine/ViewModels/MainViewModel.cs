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

        // --- PROPRIÉTÉS DES PARAMÈTRES ---
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
                    SaveConfig(); // Sauvegarde automatique
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
                    SaveConfig(); // Sauvegarde automatique
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
                    SaveConfig(); // Sauvegarde automatique (LostFocus depuis l'UI)
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
                    SaveConfig(); // Sauvegarde automatique (LostFocus depuis l'UI)
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
                    var extensions = value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                          .Select(e => e.Trim())
                                          .ToList();
                    CurrentSettings.EncryptedExtensions = extensions;
                    OnPropertyChanged(nameof(EncryptedExtensionsString));
                    SaveConfig(); // Sauvegarde automatique (LostFocus depuis l'UI)
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

        // --- PROPRIÉTÉS POUR LES PROCESSUS ET LE RESEAU ---
        private ObservableCollection<ProcessInfo> _processes;
        public ObservableCollection<ProcessInfo> Processes
        {
            get => _processes;
            set { _processes = value; OnPropertyChanged(nameof(Processes)); }
        }

        private ProcessInfo _selectedProcess;
        public ProcessInfo SelectedProcess
        {
            get => _selectedProcess;
            set
            {
                _selectedProcess = value;
                OnPropertyChanged(nameof(SelectedProcess));
                (StopProcessCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

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
        public ICommand ToggleSettingsCommand { get; }
        public ICommand AddSoftwareCommand { get; }
        public ICommand RemoveSoftwareCommand { get; }
        public ICommand StopProcessCommand { get; }
        public ICommand RefreshProcessesCommand { get; }

        public MainViewModel(ConfigManager configManager, StateTracker stateTracker, BackupEngine backupEngine, NetworkService networkService)
        {
            _configManager = configManager;
            _stateTracker = stateTracker;
            _backupEngine = backupEngine;
            _networkService = networkService;

            _syncContext = SynchronizationContext.Current;

            CurrentSettings = _configManager.LoadSettings();
            Jobs = new ObservableCollection<JobViewModel>(CurrentSettings.Jobs.Select(j => new JobViewModel(j)));

            foreach (var job in Jobs)
            {
                job.PropertyChanged += OnJobPropertyChanged;
            }

            Softwares = new ObservableCollection<string>(CurrentSettings.BusinessSoftwares);
            Processes = new ObservableCollection<ProcessInfo>();
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

            AddJobCommand = new RelayCommand(ExecuteAddJob);
            ExecuteSelectionCommand = new RelayCommand(ExecuteSelectedJob, CanExecuteSelectedJob);
            DeleteJobCommand = new RelayCommand(ExecuteDeleteJob, CanExecuteSelectedJob);
            ChangeLanguageCommand = new RelayCommand(ChangeLanguage);
            ToggleSettingsCommand = new RelayCommand(p => IsSettingsOpen = !IsSettingsOpen);
            AddSoftwareCommand = new RelayCommand(ExecuteAddSoftware);
            RemoveSoftwareCommand = new RelayCommand(ExecuteRemoveSoftware);
            StopProcessCommand = new RelayCommand(ExecuteStopProcess, CanStopProcess);
            RefreshProcessesCommand = new RelayCommand(ExecuteRefreshProcesses);

            ChangeLanguage(CurrentSettings.Language);
            ExecuteRefreshProcesses(null);
        }

        private void HandleNetworkMessage(string message)
        {
            if (message.StartsWith("[STATES_RESPONSE]|"))
            {
                string jsonPayload = message.Substring(18);
                try
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonPayload);

                    Action updateUiAction = () =>
                    {
                        RemoteStates.Clear();
                        if (dict != null)
                        {
                            foreach (var kvp in dict)
                            {
                                var jobs = new ObservableCollection<ClientJobState>();
                                try
                                {
                                    var parsedJobs = JsonSerializer.Deserialize<List<ClientJobState>>(kvp.Value.GetRawText());
                                    if (parsedJobs != null)
                                    {
                                        foreach (var j in parsedJobs) jobs.Add(j);
                                    }
                                }
                                catch { }

                                RemoteStates.Add(new RemoteClientState
                                {
                                    ClientId = kvp.Key,
                                    Jobs = jobs
                                });
                            }
                        }
                    };

                    if (_syncContext != null) _syncContext.Post(_ => updateUiAction(), null);
                    else updateUiAction();
                }
                catch (Exception ex)
                {
                    Action showErrorAction = () => CurrentFile = $"Erreur : {ex.Message}";
                    if (_syncContext != null) _syncContext.Post(_ => showErrorAction(), null);
                    else showErrorAction();
                }
            }
        }

        private void OnJobPropertyChanged(object sender, PropertyChangedEventArgs e) => SaveConfig();

        private void ChangeLanguage(object param)
        {
            string lang = param as string ?? "FR";
            CurrentSettings.Language = lang;
            LanguageService.CurrentLanguage = lang;
            SaveConfig(); // Sauvegarde automatique
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
            CurrentFile = LanguageService["MsgSlotAdded"];
        }

        private void ExecuteDeleteJob(object parameter)
        {
            if (SelectedJob != null)
            {
                SelectedJob.PropertyChanged -= OnJobPropertyChanged;
                Jobs.Remove(SelectedJob);
                SaveConfig();
                CurrentFile = LanguageService["MsgDeleted"];
                SelectedJob = null;
            }
        }

        private bool CanExecuteSelectedJob(object parameter) => SelectedJob != null;

        private async void ExecuteSelectedJob(object parameter)
        {
            if (SelectedJob == null) return;

            if (string.IsNullOrWhiteSpace(SelectedJob.SourceDirectory) || string.IsNullOrWhiteSpace(SelectedJob.TargetDirectory))
            {
                CurrentFile = LanguageService["MsgEmptyPath"];
                return;
            }

            try
            {
                CurrentFile = "Démarrage...";
                _networkService.SendMessage($"[START] {SelectedJob.Name}");
                await _backupEngine.ExecuteJobAsync(SelectedJob.Model);
                CurrentFile = "Succès !";
                _networkService.SendMessage($"[END] {SelectedJob.Name} - Succes");
            }
            catch (Exception ex)
            {
                CurrentFile = $"Erreur : {ex.Message}";
                _networkService.SendMessage($"[ERROR] {SelectedJob.Name} : {ex.Message}");
            }
        }

        public async Task ExecuteJobsAsync(List<int> ids)
        {
            try
            {
                CurrentFile = "Démarrage...";
                foreach (var id in ids)
                {
                    var jobVm = Jobs.FirstOrDefault(j => j.Id == id);
                    if (jobVm == null || string.IsNullOrWhiteSpace(jobVm.SourceDirectory) || string.IsNullOrWhiteSpace(jobVm.TargetDirectory)) continue;

                    _networkService.SendMessage($"[START] {jobVm.Name}");
                    await _backupEngine.ExecuteJobAsync(jobVm.Model);
                    _networkService.SendMessage($"[END] {jobVm.Name} - Succes");
                }
                CurrentFile = "Terminé avec succès !";
            }
            catch (Exception ex)
            {
                CurrentFile = $"Erreur : {ex.Message}";
                _networkService.SendMessage($"[ERROR] Erreur globale : {ex.Message}");
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
                SaveConfig(); // Sauvegarde automatique
            }
        }

        private void ExecuteRemoveSoftware(object parameter)
        {
            if (SelectedSoftware != null)
            {
                Softwares.Remove(SelectedSoftware);
                CurrentSettings.BusinessSoftwares = Softwares.ToList();
                SaveConfig(); // Sauvegarde automatique
            }
        }

        private void ExecuteRefreshProcesses(object parameter)
        {
            try
            {
                var runningProcesses = System.Diagnostics.Process.GetProcesses()
                    .Select(p => new ProcessInfo { Id = p.Id, Name = p.ProcessName })
                    .ToList();

                Processes.Clear();
                foreach (var process in runningProcesses) Processes.Add(process);

                _networkService.SendMessage("[GET_STATES]");
            }
            catch (Exception ex)
            {
                CurrentFile = $"Erreur rafraîchissement : {ex.Message}";
            }
        }

        private bool CanStopProcess(object parameter) => SelectedProcess != null;

        private void ExecuteStopProcess(object parameter)
        {
            if (SelectedProcess == null) return;

            try
            {
                var process = System.Diagnostics.Process.GetProcessById(SelectedProcess.Id);
                process.Kill();
                process.WaitForExit();
                ExecuteRefreshProcesses(null);
            }
            catch (Exception ex)
            {
                CurrentFile = $"Erreur arrêt : {ex.Message}";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class ProcessInfo : INotifyPropertyChanged
    {
        private int _id;
        public int Id { get => _id; set { _id = value; OnPropertyChanged(nameof(Id)); } }

        private string _name;
        public string Name { get => _name; set { _name = value; OnPropertyChanged(nameof(Name)); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class ClientJobState
    {
        [JsonPropertyName("Name")] public string Name { get; set; }
        [JsonPropertyName("SourceFilePath")] public string SourceFilePath { get; set; }
        [JsonPropertyName("TargetFilePath")] public string TargetFilePath { get; set; }
        [JsonPropertyName("State")] public string State { get; set; }
        [JsonPropertyName("TotalFilesToCopy")] public int TotalFilesToCopy { get; set; }
        [JsonPropertyName("TotalFilesSize")] public long TotalFilesSize { get; set; }
        [JsonPropertyName("NbFilesLeftToDo")] public int NbFilesLeftToDo { get; set; }
        [JsonPropertyName("Progression")] public double Progression { get; set; }
        [JsonPropertyName("LastActionDate")] public string LastActionDate { get; set; }
        [JsonPropertyName("RemainingFilesSize")] public long RemainingFilesSize { get; set; }
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
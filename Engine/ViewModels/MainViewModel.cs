using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using EasySave.Models;
using EasySave.Services;

namespace EasySave.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ConfigManager _configManager;
        private readonly BackupEngine _backupEngine;
        private readonly StateTracker _stateTracker;
        private readonly NetworkService _networkService;

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

        private Dictionary<string, string> _uiStrings;
        public Dictionary<string, string> UIStrings
        {
            get => _uiStrings;
            set { _uiStrings = value; OnPropertyChanged(nameof(UIStrings)); }
        }

        // --- PROPRIÉTÉS DES PARAMÈTRES ---
        public AppSettings CurrentSettings { get; private set; }
        public ObservableCollection<string> Softwares { get; set; }
        public List<string> AvailableLogFormats { get; } = new List<string> { "JSON", "XML" };

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
                }
            }
        }

        // Propriété gérant la chaîne d'extensions (ex: ".txt;.pdf")
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

        // --- PROPRIÉTÉS POUR LES PROCESSUS ---
        private ObservableCollection<ProcessInfo> _processes;
        public ObservableCollection<ProcessInfo> Processes
        {
            get => _processes;
            set
            {
                _processes = value;
                OnPropertyChanged(nameof(Processes));
            }
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

        // --- COMMANDES ---
        public ICommand AddJobCommand { get; }
        public ICommand ExecuteSelectionCommand { get; }
        public ICommand DeleteJobCommand { get; }
        public ICommand ChangeLanguageCommand { get; }
        public ICommand ToggleSettingsCommand { get; }
        public ICommand AddSoftwareCommand { get; }
        public ICommand RemoveSoftwareCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand StopProcessCommand { get; }
        public ICommand RefreshProcessesCommand { get; }

        public MainViewModel(ConfigManager configManager, StateTracker stateTracker, BackupEngine backupEngine, NetworkService networkService)
        {
            _configManager = configManager;
            _stateTracker = stateTracker;
            _backupEngine = backupEngine;
            _networkService = networkService;

            CurrentSettings = _configManager.LoadSettings();
            Jobs = new ObservableCollection<JobViewModel>(CurrentSettings.Jobs.Select(j => new JobViewModel(j)));

            foreach (var job in Jobs)
            {
                job.PropertyChanged += OnJobPropertyChanged;
            }

            Softwares = new ObservableCollection<string>(CurrentSettings.BusinessSoftwares);
            Processes = new ObservableCollection<ProcessInfo>();

            // Écoute de la progression classique
            _backupEngine.OnProgressUpdate += (file, remaining) =>
            {
                CurrentFile = file;
                _networkService.SendMessage($"[PROGRESS] {file}");
            };

            // Écoute des logs générés pour envoi immédiat au serveur
            EasyLog.DailyLogger.OnLogGenerated += (jobId, format, entry) =>
            {
                try
                {
                    string jsonLog = System.Text.Json.JsonSerializer.Serialize(entry);
                    // On préfixe avec [LOG] suivi des méta-données
                    _networkService.SendMessage($"[LOG]|{jobId}|{format}|{jsonLog}");
                }
                catch { }
            };

            AddJobCommand = new RelayCommand(ExecuteAddJob);
            ExecuteSelectionCommand = new RelayCommand(ExecuteSelectedJob, CanExecuteSelectedJob);
            DeleteJobCommand = new RelayCommand(ExecuteDeleteJob, CanExecuteSelectedJob);
            ChangeLanguageCommand = new RelayCommand(ChangeLanguage);
            ToggleSettingsCommand = new RelayCommand(p => IsSettingsOpen = !IsSettingsOpen);
            AddSoftwareCommand = new RelayCommand(ExecuteAddSoftware);
            RemoveSoftwareCommand = new RelayCommand(ExecuteRemoveSoftware);
            SaveSettingsCommand = new RelayCommand(SaveSettings);
            StopProcessCommand = new RelayCommand(ExecuteStopProcess, CanStopProcess);
            RefreshProcessesCommand = new RelayCommand(ExecuteRefreshProcesses);

            // Chargement initial de la langue
            ChangeLanguage(CurrentSettings.Language);

            // Rafraîchissement initial des processus
            ExecuteRefreshProcesses(null);
        }

        private void OnJobPropertyChanged(object sender, PropertyChangedEventArgs e) => SaveConfig();

        private void ChangeLanguage(object param)
        {
            string lang = param as string ?? "FR";
            CurrentSettings.Language = lang;
            CurrentSettings.Jobs = Jobs.Select(j => j.Model).ToList();
            _configManager.SaveSettings(CurrentSettings);

            if (lang == "EN")
            {
                UIStrings = new Dictionary<string, string>
                {
                    { "Title", "EasySave 2.0" },
                    { "LblName", "Name" },
                    { "LblSource", "Source Folder" },
                    { "LblTarget", "Target Folder" },
                    { "LblType", "Backup Type" },
                    { "BtnAdd", "+ Add a job" },
                    { "BtnRun", "▶ Execute selection" },
                    { "BtnDelete", "🗑 Delete" },
                    { "Recent", "Recent activity:" },
                    { "MsgEmptyPath", "❌ Error: The Source or Target folder is empty." },
                    { "MsgSlotAdded", "✅ New empty job added." },
                    { "MsgDeleted", "🗑️ Job successfully deleted." },
                    { "Settings", "⚙ Settings" },
                    { "Close", "Close" },
                    { "SaveSettings", "Save Settings" },
                    { "Softwares", "Blocking Business Softwares:" },
                    { "LblLogFormat", "Logs Format:" },
                    { "LblEncryptedExt", "Encrypted Extensions (e.g., .txt;.pdf):" }
                };
            }
            else
            {
                UIStrings = new Dictionary<string, string>
                {
                    { "Title", "EasySave 2.0" },
                    { "LblName", "Nom du travail" },
                    { "LblSource", "Dossier Source" },
                    { "LblTarget", "Dossier Cible" },
                    { "LblType", "Type de sauvegarde" },
                    { "BtnAdd", "+ Ajouter un travail" },
                    { "BtnRun", "▶ Lancer la sélection" },
                    { "BtnDelete", "🗑 Supprimer" },
                    { "Recent", "Activit\u00e9 r\u00e9cente :" },
                    { "MsgEmptyPath", "❌ Erreur : Le dossier Source ou Cible est vide." },
                    { "MsgSlotAdded", "✅ Nouveau travail vide ajout\u00e9." },
                    { "MsgDeleted", "🗑️ Travail supprim\u00e9 avec succ\u00e8s." },
                    { "Settings", "⚙ Param\u00e8tres" },
                    { "Close", "Fermer" },
                    { "SaveSettings", "Enregistrer les param\u00e8tres" },
                    { "Softwares", "Logiciels m\u00e9tier bloquants :" },
                    { "LblLogFormat", "Format des logs :" },
                    { "LblEncryptedExt", "Extensions \u00e0 chiffrer (ex: .txt;.pdf) :" }
                };
            }
        }

        private void SaveSettings(object parameter)
        {
            CurrentSettings.BusinessSoftwares = Softwares.ToList();
            CurrentSettings.Jobs = Jobs.Select(j => j.Model).ToList();
            _configManager.SaveSettings(CurrentSettings);
            IsSettingsOpen = false;
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
            CurrentFile = UIStrings["MsgSlotAdded"];
        }

        private void ExecuteDeleteJob(object parameter)
        {
            if (SelectedJob != null)
            {
                SelectedJob.PropertyChanged -= OnJobPropertyChanged;
                Jobs.Remove(SelectedJob);
                SaveConfig();
                CurrentFile = UIStrings["MsgDeleted"];
                SelectedJob = null;
            }
        }

        private bool CanExecuteSelectedJob(object parameter) => SelectedJob != null;

        private async void ExecuteSelectedJob(object parameter)
        {
            if (SelectedJob == null) return;

            if (string.IsNullOrWhiteSpace(SelectedJob.SourceDirectory) || string.IsNullOrWhiteSpace(SelectedJob.TargetDirectory))
            {
                CurrentFile = UIStrings["MsgEmptyPath"];
                return;
            }

            try
            {
                CurrentFile = "⏳ Démarrage de la sauvegarde...";
                _networkService.SendMessage($"[START] {SelectedJob.Name}");

                await _backupEngine.ExecuteJobAsync(SelectedJob.Model);

                CurrentFile = "✅ Sauvegarde terminée avec succès !";
                _networkService.SendMessage($"[END] {SelectedJob.Name} - Succès");
            }
            catch (Exception ex)
            {
                CurrentFile = $"❌ Erreur : {ex.Message}";
                _networkService.SendMessage($"[ERROR] {SelectedJob.Name} : {ex.Message}");
            }
        }

        public async Task ExecuteJobsAsync(List<int> ids)
        {
            try
            {
                CurrentFile = "⏳ Démarrage des sauvegardes demandées...";
                foreach (var id in ids)
                {
                    var jobVm = Jobs.FirstOrDefault(j => j.Id == id);
                    if (jobVm == null || string.IsNullOrWhiteSpace(jobVm.SourceDirectory) || string.IsNullOrWhiteSpace(jobVm.TargetDirectory)) continue;

                    _networkService.SendMessage($"[START] {jobVm.Name}");
                    await _backupEngine.ExecuteJobAsync(jobVm.Model);
                    _networkService.SendMessage($"[END] {jobVm.Name} - Succès");
                }
                CurrentFile = "✅ Toutes les sauvegardes sont terminées avec succès !";
            }
            catch (Exception ex)
            {
                CurrentFile = $"❌ Erreur : {ex.Message}";
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
                NewSoftware = "";
            }
        }

        private void ExecuteRemoveSoftware(object parameter)
        {
            if (SelectedSoftware != null) Softwares.Remove(SelectedSoftware);
        }

        // --- GESTION DES PROCESSUS ---
        private void ExecuteRefreshProcesses(object parameter)
        {
            try
            {
                var runningProcesses = System.Diagnostics.Process.GetProcesses()
                    .Select(p => new ProcessInfo { Id = p.Id, Name = p.ProcessName })
                    .ToList();

                Processes.Clear();
                foreach (var process in runningProcesses)
                {
                    Processes.Add(process);
                }
            }
            catch (Exception ex)
            {
                CurrentFile = $"❌ Erreur lors du rafraîchissement des processus : {ex.Message}";
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
                CurrentFile = $"✅ Processus '{SelectedProcess.Name}' arrêté avec succès.";
                ExecuteRefreshProcesses(null);
            }
            catch (Exception ex)
            {
                CurrentFile = $"❌ Erreur lors de l'arrêt du processus : {ex.Message}";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // Classe pour représenter un processus
    public class ProcessInfo : INotifyPropertyChanged
    {
        private int _id;
        public int Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(nameof(Id)); }
        }

        private string _name;
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
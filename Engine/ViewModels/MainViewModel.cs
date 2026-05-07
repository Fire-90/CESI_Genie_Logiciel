using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
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

        // Context used to safely update the UI thread from background tasks
        private readonly SynchronizationContext _uiContext;

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

        public string EncryptedExtensionsString
        {
            get
            {
                if (CurrentSettings?.EncryptedExtensions == null) return "";
                return string.Join(";", CurrentSettings.EncryptedExtensions);
            }
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

        public ICommand AddJobCommand { get; }
        public ICommand ExecuteSelectionCommand { get; }
        public ICommand DeleteJobCommand { get; }
        public ICommand ChangeLanguageCommand { get; }
        public ICommand ToggleSettingsCommand { get; }
        public ICommand AddSoftwareCommand { get; }
        public ICommand RemoveSoftwareCommand { get; }
        public ICommand SaveSettingsCommand { get; }

        public MainViewModel(ConfigManager configManager, StateTracker stateTracker, BackupEngine backupEngine)
        {
            _configManager = configManager;
            _stateTracker = stateTracker;
            _backupEngine = backupEngine;

            _uiContext = SynchronizationContext.Current;

            CurrentSettings = _configManager.LoadSettings();

            Jobs = new ObservableCollection<JobViewModel>(CurrentSettings.Jobs.Select(j => new JobViewModel(j)));

            foreach (var job in Jobs)
            {
                job.PropertyChanged += OnJobPropertyChanged;
            }

            Softwares = new ObservableCollection<string>(CurrentSettings.BusinessSoftwares);

            _backupEngine.OnProgressUpdate += (file, remaining) => { CurrentFile = file; };

            _backupEngine.OnJobProgress += (jobName, progress) =>
            {
                if (_uiContext != null)
                {
                    _uiContext.Post(_ =>
                    {
                        var jobVm = Jobs.FirstOrDefault(j => j.Name == jobName);
                        if (jobVm != null) jobVm.Progress = progress;
                    }, null);
                }
            };

            AddJobCommand = new RelayCommand(ExecuteAddJob);
            ExecuteSelectionCommand = new RelayCommand(ExecuteSelectedJob);
            DeleteJobCommand = new RelayCommand(ExecuteDeleteJob, CanExecuteSelectedJob);
            ChangeLanguageCommand = new RelayCommand(ChangeLanguage);

            ToggleSettingsCommand = new RelayCommand(p => IsSettingsOpen = !IsSettingsOpen);

            AddSoftwareCommand = new RelayCommand(p =>
            {
                if (!string.IsNullOrWhiteSpace(NewSoftware) && !Softwares.Contains(NewSoftware))
                {
                    Softwares.Add(NewSoftware);
                    NewSoftware = "";
                }
            });

            RemoveSoftwareCommand = new RelayCommand(p =>
            {
                if (SelectedSoftware != null) Softwares.Remove(SelectedSoftware);
            });

            SaveSettingsCommand = new RelayCommand(SaveSettings);

            ChangeLanguage(CurrentSettings.Language);
        }

        private void OnJobPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Ignore UI-only property changes to prevent recursive save operations
            if (e.PropertyName == "Progress" || e.PropertyName == "IsSelected")
            {
                return;
            }

            SaveConfig();
        }

        private void ChangeLanguage(object param)
        {
            string lang = param as string ?? "EN";

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
                    { "Recent", "Activité récente :" },
                    { "MsgEmptyPath", "❌ Erreur : Le dossier Source ou Cible est vide." },
                    { "MsgSlotAdded", "✅ Nouveau travail vide ajouté." },
                    { "MsgDeleted", "🗑️ Travail supprimé avec succès." },
                    { "Settings", "⚙ Paramètres" },
                    { "Close", "Fermer" },
                    { "SaveSettings", "Enregistrer les paramètres" },
                    { "Softwares", "Logiciels métier bloquants :" },
                    { "LblLogFormat", "Format des logs :" },
                    { "LblEncryptedExt", "Extensions à chiffrer (ex: .txt;.pdf) :" }
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
            var newModel = new BackupJob
            {
                Id = newId,
                Name = $"Save {newId}",
                SourceDirectory = "",
                TargetDirectory = "",
                Type = BackupType.Full
            };

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
            var jobsToRun = Jobs.Where(j => j.IsSelected).ToList();

            if (!jobsToRun.Any())
            {
                CurrentFile = "⚠️ Please select at least one job using the checkboxes.";
                return;
            }

            try
            {
                CurrentFile = "⏳ Starting background backups...";
                var tasks = new List<Task>();

                foreach (var jobVm in jobsToRun)
                {
                    if (string.IsNullOrWhiteSpace(jobVm.SourceDirectory) || string.IsNullOrWhiteSpace(jobVm.TargetDirectory)) continue;

                    jobVm.Progress = 0;

                    tasks.Add(Task.Run(() => _backupEngine.ExecuteJobAsync(jobVm.Model)));
                }

                await Task.WhenAll(tasks);
                CurrentFile = "✅ All selected backups completed successfully!";
            }
            catch (Exception ex)
            {
                CurrentFile = $"❌ Error: {ex.Message}";
            }
        }

        public async Task ExecuteJobsAsync(List<int> ids)
        {
            try
            {
                CurrentFile = "⏳ Starting requested backups...";
                var tasks = new List<Task>();

                foreach (var id in ids)
                {
                    var jobVm = Jobs.FirstOrDefault(j => j.Id == id);
                    if (jobVm == null || string.IsNullOrWhiteSpace(jobVm.SourceDirectory) || string.IsNullOrWhiteSpace(jobVm.TargetDirectory)) continue;

                    jobVm.Progress = 0;

                    tasks.Add(Task.Run(() => _backupEngine.ExecuteJobAsync(jobVm.Model)));
                }

                await Task.WhenAll(tasks);
                CurrentFile = "✅ All backups completed successfully!";
            }
            catch (Exception ex)
            {
                CurrentFile = $"❌ Error: {ex.Message}";
            }
        }

        public void SaveConfig()
        {
            CurrentSettings.Jobs = Jobs.Select(j => j.Model).ToList();
            _configManager.SaveSettings(CurrentSettings);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
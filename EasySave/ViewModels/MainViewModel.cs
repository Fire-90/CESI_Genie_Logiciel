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

        public ObservableCollection<JobViewModel> Jobs { get; }

        // Liste pour alimenter la liste déroulante "Type"
        public List<BackupType> AvailableTypes { get; } = new List<BackupType> { BackupType.Full, BackupType.Differential };

        // --- GESTION DE LA SÉLECTION ---
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
                    (ExecuteSelectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
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

        // --- SYSTÈME DE LANGUES (I18N) ---
        private Dictionary<string, string> _uiStrings;
        public Dictionary<string, string> UIStrings
        {
            get => _uiStrings;
            set
            {
                _uiStrings = value;
                OnPropertyChanged(nameof(UIStrings));
            }
        }

        // --- COMMANDES ---
        public ICommand AddJobCommand { get; }
        public ICommand ExecuteSelectionCommand { get; }
        public ICommand ChangeLanguageCommand { get; }

        public MainViewModel(ConfigManager configManager, StateTracker stateTracker, BackupEngine backupEngine)
        {
            _configManager = configManager;
            _stateTracker = stateTracker;
            _backupEngine = backupEngine;

            var loadedJobs = _configManager.LoadConfig();
            Jobs = new ObservableCollection<JobViewModel>(loadedJobs.Select(j => new JobViewModel(j)));

            _backupEngine.OnProgressUpdate += (file, remaining) => { CurrentFile = file; };

            AddJobCommand = new RelayCommand(ExecuteAddJob);
            ExecuteSelectionCommand = new RelayCommand(ExecuteSelectedJob, CanExecuteSelectedJob);
            ChangeLanguageCommand = new RelayCommand(ChangeLanguage);

            // Charger le français par défaut
            ChangeLanguage("FR");
        }

        // --- LOGIQUE DES COMMANDES ---

        private void ChangeLanguage(object param)
        {
            string lang = param as string;
            if (lang == "EN")
            {
                UIStrings = new Dictionary<string, string>
                {
                    { "Title", "EasySave 2.0 - ProSoft" },
                    { "LblName", "Name" },
                    { "LblSource", "Source Folder" },
                    { "LblTarget", "Target Folder" },
                    { "LblType", "Backup Type" },
                    { "BtnAdd", "+ Add a job" },
                    { "BtnRun", "▶ Execute selection" },
                    { "Recent", "Recent activity:" }
                };
            }
            else // FR par défaut
            {
                UIStrings = new Dictionary<string, string>
                {
                    { "Title", "EasySave 2.0 - ProSoft" },
                    { "LblName", "Nom du travail" },
                    { "LblSource", "Dossier Source" },
                    { "LblTarget", "Dossier Cible" },
                    { "LblType", "Type de sauvegarde" },
                    { "BtnAdd", "+ Ajouter un travail" },
                    { "BtnRun", "▶ Lancer la sélection" },
                    { "Recent", "Activité récente :" }
                };
            }
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
            Jobs.Add(newViewModel);
            SaveConfig();

            // Sélectionner automatiquement le nouveau travail pour que l'utilisateur puisse le modifier en bas !
            SelectedJob = newViewModel;
        }

        private bool CanExecuteSelectedJob(object parameter) => SelectedJob != null;

        private async void ExecuteSelectedJob(object parameter)
        {
            if (SelectedJob == null || string.IsNullOrWhiteSpace(SelectedJob.SourceDirectory)) return;
            try { await _backupEngine.ExecuteJobAsync(SelectedJob.Model); }
            catch (Exception ex) { Console.WriteLine($"Erreur : {ex.Message}"); }
        }

        public void SaveConfig()
        {
            var models = Jobs.Select(j => j.Model).ToList();
            _configManager.SaveConfig(models);
        }

        public void UpdateJobName(string oldName, string newName)
        {
            var vm = Jobs.FirstOrDefault(j => j.Name == oldName);
            if (vm != null) { vm.Name = newName; SaveConfig(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
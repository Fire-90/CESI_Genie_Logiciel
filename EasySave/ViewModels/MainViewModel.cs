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

        // --- PROPRIÉTÉS DES TRAVAUX ---
        public ObservableCollection<JobViewModel> Jobs { get; }
        public List<BackupType> AvailableTypes { get; } = new List<BackupType> { BackupType.Full, BackupType.Differential };

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

        // --- PROPRIÉTÉS DES PARAMÈTRES (NOUVEAU) ---
        public AppSettings CurrentSettings { get; private set; }
        public ObservableCollection<string> Softwares { get; set; }

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

        // --- COMMANDES ---
        public ICommand AddJobCommand { get; }
        public ICommand ExecuteSelectionCommand { get; }
        public ICommand DeleteJobCommand { get; }
        public ICommand ChangeLanguageCommand { get; }

        // Commandes des paramètres (NOUVEAU)
        public ICommand ToggleSettingsCommand { get; }
        public ICommand AddSoftwareCommand { get; }
        public ICommand RemoveSoftwareCommand { get; }
        public ICommand SaveSettingsCommand { get; }

        public MainViewModel(ConfigManager configManager, StateTracker stateTracker, BackupEngine backupEngine)
        {
            _configManager = configManager;
            _stateTracker = stateTracker;
            _backupEngine = backupEngine;

            // Chargement des travaux
            var loadedJobs = _configManager.LoadConfig();
            Jobs = new ObservableCollection<JobViewModel>(loadedJobs.Select(j => new JobViewModel(j)));

            // Chargement des paramètres (NOUVEAU)
            CurrentSettings = _configManager.LoadSettings();
            Softwares = new ObservableCollection<string>(CurrentSettings.BusinessSoftwares);

            _backupEngine.OnProgressUpdate += (file, remaining) => { CurrentFile = file; };

            // Initialisation des commandes des travaux
            AddJobCommand = new RelayCommand(ExecuteAddJob);
            ExecuteSelectionCommand = new RelayCommand(ExecuteSelectedJob, CanExecuteSelectedJob);
            DeleteJobCommand = new RelayCommand(ExecuteDeleteJob, CanExecuteSelectedJob);
            ChangeLanguageCommand = new RelayCommand(ChangeLanguage);

            // Initialisation des commandes des paramètres (NOUVEAU)
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

            // Applique la langue sauvegardée au lieu de forcer "FR"
            ChangeLanguage(CurrentSettings.Language);
        }

        // --- GESTION DE LA LANGUE ET PARAMÈTRES ---
        private void ChangeLanguage(object param)
        {
            string lang = param as string ?? "FR";

            // On sauvegarde le choix de langue dynamiquement
            CurrentSettings.Language = lang;
            _configManager.SaveSettings(CurrentSettings);

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
                    { "BtnDelete", "🗑 Delete" },
                    { "Recent", "Recent activity:" },
                    { "MsgEmpty", "⚠️ Please fill all empty fields before creating a new job." },
                    { "MsgDeleted", "🗑️ Job successfully deleted." },
                    // Nouveaux textes pour les paramètres
                    { "Settings", "⚙ Settings" },
                    { "Close", "Close" },
                    { "SaveSettings", "Save Settings" },
                    { "Softwares", "Blocking Business Softwares:" }
                };
            }
            else
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
                    { "BtnDelete", "🗑 Supprimer" },
                    { "Recent", "Activité récente :" },
                    { "MsgEmpty", "⚠️ Veuillez remplir tous les champs vides avant d'ajouter un nouveau travail." },
                    { "MsgDeleted", "🗑️ Travail supprimé avec succès." },
                    // Nouveaux textes pour les paramètres
                    { "Settings", "⚙ Paramètres" },
                    { "Close", "Fermer" },
                    { "SaveSettings", "Enregistrer les paramètres" },
                    { "Softwares", "Logiciels métier bloquants :" }
                };
            }
        }

        private void SaveSettings(object parameter)
        {
            CurrentSettings.BusinessSoftwares = Softwares.ToList();
            _configManager.SaveSettings(CurrentSettings);
            IsSettingsOpen = false; // Ferme le menu après avoir cliqué sur sauvegarder
        }

        // --- LOGIQUE D'AJOUT AVEC VÉRIFICATION ---
        private void ExecuteAddJob(object parameter)
        {
            bool hasEmptyFields = Jobs.Any(j =>
                string.IsNullOrWhiteSpace(j.Name) ||
                string.IsNullOrWhiteSpace(j.SourceDirectory) ||
                string.IsNullOrWhiteSpace(j.TargetDirectory));

            if (hasEmptyFields)
            {
                CurrentFile = UIStrings["MsgEmpty"];
                return;
            }

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

            SelectedJob = newViewModel;
        }

        // --- LOGIQUE DE SUPPRESSION ---
        private void ExecuteDeleteJob(object parameter)
        {
            if (SelectedJob != null)
            {
                Jobs.Remove(SelectedJob);
                SaveConfig();
                CurrentFile = UIStrings["MsgDeleted"];
                SelectedJob = null;
            }
        }

        private bool CanExecuteSelectedJob(object parameter) => SelectedJob != null;

        private async void ExecuteSelectedJob(object parameter)
        {
            if (SelectedJob == null || string.IsNullOrWhiteSpace(SelectedJob.SourceDirectory)) return;

            try
            {
                CurrentFile = "⏳ Démarrage de la sauvegarde...";
                await _backupEngine.ExecuteJobAsync(SelectedJob.Model);
                CurrentFile = "✅ Sauvegarde terminée avec succès !";
            }
            catch (Exception ex)
            {
                CurrentFile = $"❌ Erreur : {ex.Message}";
            }
        }

        // --- POUR LE TERMINAL (.\Graphic.exe "1-2") ---
        public async Task ExecuteJobsAsync(List<int> ids)
        {
            try
            {
                CurrentFile = "⏳ Démarrage des sauvegardes demandées...";
                foreach (var id in ids)
                {
                    var jobVm = Jobs.FirstOrDefault(j => j.Id == id);
                    if (jobVm == null || string.IsNullOrWhiteSpace(jobVm.SourceDirectory)) continue;
                    await _backupEngine.ExecuteJobAsync(jobVm.Model);
                }
                CurrentFile = "✅ Toutes les sauvegardes sont terminées avec succès !";
            }
            catch (Exception ex) { CurrentFile = $"❌ Erreur : {ex.Message}"; }
        }

        public void SaveConfig()
        {
            var models = Jobs.Select(j => j.Model).ToList();
            _configManager.SaveConfig(models);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
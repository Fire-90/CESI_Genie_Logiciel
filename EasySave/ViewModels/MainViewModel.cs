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

                    // On met à jour l'état (grisé ou non) des boutons qui dépendent de la sélection
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

        // --- COMMANDES ---
        public ICommand AddJobCommand { get; }
        public ICommand ExecuteSelectionCommand { get; }
        public ICommand DeleteJobCommand { get; }
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
            DeleteJobCommand = new RelayCommand(ExecuteDeleteJob, CanExecuteSelectedJob);
            ChangeLanguageCommand = new RelayCommand(ChangeLanguage);

            ChangeLanguage("FR");
        }

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
                    { "BtnDelete", "🗑 Delete" },
                    { "Recent", "Recent activity:" },
                    { "MsgEmpty", "⚠️ Please fill all empty fields before creating a new job." },
                    { "MsgDeleted", "🗑️ Job successfully deleted." }
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
                    { "MsgDeleted", "🗑️ Travail supprimé avec succès." }
                };
            }
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

        // --- NOUVELLE LOGIQUE DE SUPPRESSION ---
        private void ExecuteDeleteJob(object parameter)
        {
            if (SelectedJob != null)
            {
                Jobs.Remove(SelectedJob); // Retire de la liste visuelle
                SaveConfig();             // Met à jour le JSON
                CurrentFile = UIStrings["MsgDeleted"];
                SelectedJob = null;       // Désélectionne pour griser les boutons
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

        // Pour le terminal (.\Graphic.exe "1-2")
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
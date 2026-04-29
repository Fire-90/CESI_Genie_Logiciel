using EasySave.Models;
using EasySave.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input; // Requis pour ICommand

namespace EasySave.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ConfigManager _configManager;
        private readonly BackupEngine _backupEngine;
        private readonly StateTracker _stateTracker;

        // Remplacement de List par ObservableCollection pour mettre à jour l'UI automatiquement
        public ObservableCollection<JobViewModel> Jobs { get; }

        // --- GESTION DE LA SÉLECTION DANS LE DATAGRID ---
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

                    // NOUVELLE LIGNE : On prévient le bouton qu'il doit revérifier son état (grisé ou non)
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

        // Inputs used when creating a NEW job
        private string _newSourceDirectory = "";
        public string NewSourceDirectory
        {
            get => _newSourceDirectory;
            set
            {
                if (_newSourceDirectory != value)
                {
                    _newSourceDirectory = value;
                    OnPropertyChanged(nameof(NewSourceDirectory));
                }
            }
        }

        private string _newTargetDirectory = "";
        public string NewTargetDirectory
        {
            get => _newTargetDirectory;
            set
            {
                if (_newTargetDirectory != value)
                {
                    _newTargetDirectory = value;
                    OnPropertyChanged(nameof(NewTargetDirectory));
                }
            }
        }

        // --- COMMANDES POUR LES BOUTONS DE L'INTERFACE ---
        public ICommand AddJobCommand { get; }
        public ICommand ExecuteSelectionCommand { get; }

        public MainViewModel(ConfigManager configManager, StateTracker stateTracker, BackupEngine backupEngine)
        {
            _configManager = configManager;
            _stateTracker = stateTracker;
            _backupEngine = backupEngine;

            // Chargement des modèles et initialisation de l'ObservableCollection
            var loadedJobs = _configManager.LoadConfig();
            Jobs = new ObservableCollection<JobViewModel>(loadedJobs.Select(j => new JobViewModel(j)));

            // Subscribe to item property changes so edits to Source/Target are persisted
            foreach (var jvm in Jobs) jvm.PropertyChanged += Job_PropertyChanged;
            Jobs.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (JobViewModel nv in e.NewItems) nv.PropertyChanged += Job_PropertyChanged;
                }
                if (e.OldItems != null)
                {
                    foreach (JobViewModel ov in e.OldItems) ov.PropertyChanged -= Job_PropertyChanged;
                }
            };

            // Abonnement à la progression du moteur
            _backupEngine.OnProgressUpdate += (file, remaining) =>
            {
                CurrentFile = file;
            };

            // Initialisation des Commandes
            AddJobCommand = new RelayCommand(ExecuteAddJob);
            ExecuteSelectionCommand = new RelayCommand(ExecuteSelectedJob, CanExecuteSelectedJob);
        }

        private void Job_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Persist changes when key job properties change (Name, SourceDirectory, TargetDirectory, Type)
            if (e.PropertyName == nameof(JobViewModel.SourceDirectory) ||
                e.PropertyName == nameof(JobViewModel.TargetDirectory) ||
                e.PropertyName == nameof(JobViewModel.Name) ||
                e.PropertyName == nameof(JobViewModel.Type))
            {
                SaveConfig();
            }
        }

        // --- LOGIQUE DES BOUTONS ---

        private void ExecuteAddJob(object parameter)
        {
            // Trouver le plus grand ID et faire +1 (gère les travaux illimités)
            int newId = Jobs.Count > 0 ? Jobs.Max(j => j.Id) + 1 : 1;

            var newModel = new BackupJob
            {
                Id = newId,
                Name = $"Nouveau Travail {newId}",
                SourceDirectory = NewSourceDirectory ?? "",
                TargetDirectory = NewTargetDirectory ?? "",
                Type = BackupType.Full
            };

            var newViewModel = new JobViewModel(newModel);
            newViewModel.PropertyChanged += Job_PropertyChanged;
            Jobs.Add(newViewModel);
            SaveConfig(); // Sauvegarde immédiate dans le JSON

            // Clear inputs after creating
            NewSourceDirectory = "";
            NewTargetDirectory = "";
        }

        private bool CanExecuteSelectedJob(object parameter)
        {
            // Le bouton "Lancer" ne sera actif QUE si un travail est sélectionné dans la grille
            return SelectedJob != null;
        }

        private async void ExecuteSelectedJob(object parameter)
        {
            // Sécurité : on ne lance rien si la source n'est pas configurée
            if (SelectedJob == null || string.IsNullOrWhiteSpace(SelectedJob.SourceDirectory))
                return;

            try
            {
                await _backupEngine.ExecuteJobAsync(SelectedJob.Model);
            }
            catch (Exception ex)
            {
                // Gestion des erreurs (à afficher plus tard dans une MessageBox par exemple)
                Console.WriteLine($"Erreur lors de la sauvegarde : {ex.Message}");
            }
        }

        public async Task ExecuteJobsAsync(List<int> ids)
        {
            foreach (var id in ids)
            {
                var jobVm = Jobs.FirstOrDefault(j => j.Id == id);
                if (jobVm == null) continue;

                if (string.IsNullOrWhiteSpace(jobVm.SourceDirectory)) continue;

                try
                {
                    await _backupEngine.ExecuteJobAsync(jobVm.Model);
                }
                catch
                {
                    throw;
                }
            }
        }

        public void SaveConfig()
        {
            var models = Jobs.Select(j => j.Model).ToList();
            _configManager.SaveConfig(models);
        }

        public void UpdateJobName(string oldName, string newName)
        {
            var vm = Jobs.FirstOrDefault(j => j.Name == oldName);
            if (vm != null)
            {
                vm.Name = newName;
                SaveConfig();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
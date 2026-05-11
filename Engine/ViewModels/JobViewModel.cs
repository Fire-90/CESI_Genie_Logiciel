using System.ComponentModel;
using EasySave.Models;

namespace EasySave.ViewModels
{
    public class JobViewModel : INotifyPropertyChanged
    {
        public BackupJob Model { get; }
        public JobViewModel(BackupJob model)
        {
            Model = model;
        }
        public int Id => Model.Id;

        public string Name
        {
            get => Model.Name;
            set { if (Model.Name != value) { Model.Name = value; OnPropertyChanged(nameof(Name)); } }
        }
        public string SourceDirectory
        {
            get => Model.SourceDirectory;
            set { if (Model.SourceDirectory != value) { Model.SourceDirectory = value; OnPropertyChanged(nameof(SourceDirectory)); } }
        }
        public string TargetDirectory
        {
            get => Model.TargetDirectory;
            set { if (Model.TargetDirectory != value) { Model.TargetDirectory = value; OnPropertyChanged(nameof(TargetDirectory)); } }
        }
        public BackupType Type
        {
            get => Model.Type;
            set { if (Model.Type != value) { Model.Type = value; OnPropertyChanged(nameof(Type)); } }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        private int _progress;
        public int Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(nameof(Progress)); }
        }

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set { _isRunning = value; OnPropertyChanged(nameof(IsRunning)); }
        }

        private bool _isWaiting;
        public bool IsWaiting
        {
            get => _isWaiting;
            set { _isWaiting = value; OnPropertyChanged(nameof(IsWaiting)); }
        }

        private bool _isBlocked;
        public bool IsBlocked
        {
            get => _isBlocked;
            set { _isBlocked = value; OnPropertyChanged(nameof(IsBlocked)); }
        }

        // PAUSE MANUELLE (Utilisateur)
        private bool _isPaused;
        public bool IsPaused
        {
            get => _isPaused;
            set { _isPaused = value; OnPropertyChanged(nameof(IsPaused)); }
        }

        // PAUSE AUTOMATIQUE (Logiciel métier)
        private bool _isSoftwareSuspended;
        public bool IsSoftwareSuspended
        {
            get => _isSoftwareSuspended;
            set { _isSoftwareSuspended = value; OnPropertyChanged(nameof(IsSoftwareSuspended)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
          PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
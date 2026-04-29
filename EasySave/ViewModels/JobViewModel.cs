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
            set
            {
                if (Model.Name != value)
                {
                    Model.Name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }

        public string SourceDirectory
        {
            get => Model.SourceDirectory;
            set
            {
                if (Model.SourceDirectory != value)
                {
                    Model.SourceDirectory = value;
                    OnPropertyChanged(nameof(SourceDirectory));
                }
            }
        }

        public string TargetDirectory
        {
            get => Model.TargetDirectory;
            set
            {
                if (Model.TargetDirectory != value)
                {
                    Model.TargetDirectory = value;
                    OnPropertyChanged(nameof(TargetDirectory));
                }
            }
        }

        public BackupType Type
        {
            get => Model.Type;
            set
            {
                if (Model.Type != value)
                {
                    Model.Type = value;
                    OnPropertyChanged(nameof(Type));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName) =>
          PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
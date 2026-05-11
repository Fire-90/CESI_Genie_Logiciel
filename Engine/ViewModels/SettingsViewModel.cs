using EasySave.Services;
using EasySave.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;

namespace EasySave.ViewModels
{
    public class SettingsViewModel : INotifyPropertyChanged
    {
        private readonly ConfigManager _configManager;
        private readonly JobManagementViewModel _jobVM; // Permet de synchroniser la liste des jobs lors du changement de langue

        public LanguageService LanguageService { get; }

        public AppSettings CurrentSettings { get; private set; }
        public ObservableCollection<string> Softwares { get; set; }

        public List<string> AvailableLogFormats { get; } = new List<string> { "JSON", "XML" };
        public List<string> AvailableLogDestinations { get; } = new List<string> { "LocalOnly", "ServerOnly", "LocalAndServer" };
        public List<string> AvailableSizeUnits { get; } = new List<string> { "Ko", "Mo", "Go" };

        public ICommand ChangeLanguageCommand { get; }
        public ICommand AddSoftwareCommand { get; }
        public ICommand RemoveSoftwareCommand { get; }

        public SettingsViewModel(ConfigManager configManager, LanguageService languageService, JobManagementViewModel jobVM)
        {
            _configManager = configManager;
            LanguageService = languageService;
            _jobVM = jobVM;

            CurrentSettings = _configManager.LoadSettings();
            Softwares = new ObservableCollection<string>(CurrentSettings.BusinessSoftwares);

            ChangeLanguageCommand = new RelayCommand(ChangeLanguage);
            AddSoftwareCommand = new RelayCommand(ExecuteAddSoftware);
            RemoveSoftwareCommand = new RelayCommand(ExecuteRemoveSoftware);

            LanguageService.CurrentLanguage = CurrentSettings.Language;
        }

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

        public long MaxParallelFileSizeLimit
        {
            get => CurrentSettings?.MaxParallelFileSizeLimit ?? 50000;
            set
            {
                if (CurrentSettings != null && CurrentSettings.MaxParallelFileSizeLimit != value)
                {
                    CurrentSettings.MaxParallelFileSizeLimit = value;
                    OnPropertyChanged(nameof(MaxParallelFileSizeLimit));
                    SaveConfig();
                }
            }
        }

        public string MaxParallelFileSizeLimitUnit
        {
            get => CurrentSettings?.MaxParallelFileSizeLimitUnit ?? "Ko";
            set
            {
                if (CurrentSettings != null && CurrentSettings.MaxParallelFileSizeLimitUnit != value)
                {
                    CurrentSettings.MaxParallelFileSizeLimitUnit = value;
                    OnPropertyChanged(nameof(MaxParallelFileSizeLimitUnit));
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

        private string _newSoftware;
        public string NewSoftware
        {
            get => _newSoftware;
            set { _newSoftware = value; OnPropertyChanged(nameof(NewSoftware)); }
        }

        public string SelectedSoftware { get; set; }

        private void ChangeLanguage(object param)
        {
            string lang = param as string ?? "EN";
            CurrentSettings.Language = lang;
            if (_jobVM != null)
            {
                CurrentSettings.Jobs = _jobVM.Jobs.Select(j => j.Model).ToList();
            }
            _configManager.SaveSettings(CurrentSettings);
            LanguageService.CurrentLanguage = lang;
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

        public void SaveConfig()
        {
            if (_jobVM != null) CurrentSettings.Jobs = _jobVM.Jobs.Select(j => j.Model).ToList();
            _configManager.SaveSettings(CurrentSettings);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
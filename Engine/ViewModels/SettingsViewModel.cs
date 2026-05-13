using EasySave.Models;
using EasySave.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace EasySave.ViewModels
{
    public class SettingsViewModel : INotifyPropertyChanged
    {
        private readonly SettingService _configManager;
        private readonly JobManagementViewModel _jobVM;
        private readonly NetworkService _networkService;
        private readonly SynchronizationContext _syncContext;

        public LanguageService LanguageService { get; }

        public AppSettings CurrentSettings { get; private set; }
        public ObservableCollection<string> Softwares { get; set; }

        public List<string> AvailableLogFormats { get; } = new List<string> { "JSON", "XML" };
        public List<string> AvailableLogDestinations { get; } = new List<string> { "LocalOnly", "ServerOnly", "LocalAndServer" };
        public List<string> AvailableSizeUnits { get; } = new List<string> { "Ko", "Mo", "Go" };

        // --- STATUT DE CONNEXION ---
        private ConnectionStatus _currentConnectionStatus = ConnectionStatus.Disconnected;

        private string _connectionStatusText;
        public string ConnectionStatusText { get => _connectionStatusText; set { _connectionStatusText = value; OnPropertyChanged(nameof(ConnectionStatusText)); } }

        private string _connectionStatusColor = "#E74C3C";
        public string ConnectionStatusColor { get => _connectionStatusColor; set { _connectionStatusColor = value; OnPropertyChanged(nameof(ConnectionStatusColor)); } }

        // --- ENCAPSULATION DES PARAMÈTRES POUR FORCER LA SAUVEGARDE EN TEMPS RÉEL ---

        public string Language
        {
            get => CurrentSettings.Language;
            set { CurrentSettings.Language = value; LanguageService.CurrentLanguage = value; OnPropertyChanged(nameof(Language)); SaveConfig(); }
        }

        public string PauseBehavior
        {
            get => CurrentSettings.PauseBehavior;
            set { CurrentSettings.PauseBehavior = value; OnPropertyChanged(nameof(PauseBehavior)); SaveConfig(); }
        }

        public string LogFormat
        {
            get => CurrentSettings.LogFormat;
            set { CurrentSettings.LogFormat = value; OnPropertyChanged(nameof(LogFormat)); SaveConfig(); }
        }

        public string LogDestination
        {
            get => CurrentSettings.LogDestination;
            set { CurrentSettings.LogDestination = value; OnPropertyChanged(nameof(LogDestination)); SaveConfig(); }
        }

        public string ServerIP
        {
            get => CurrentSettings.ServerIP;
            set { CurrentSettings.ServerIP = value; OnPropertyChanged(nameof(ServerIP)); SaveConfig(); }
        }

        public string ClientName
        {
            get => CurrentSettings.ClientName;
            set { CurrentSettings.ClientName = value; OnPropertyChanged(nameof(ClientName)); SaveConfig(); }
        }

        public long MaxParallelFileSizeLimit
        {
            get => CurrentSettings.MaxParallelFileSizeLimit;
            set { CurrentSettings.MaxParallelFileSizeLimit = value; OnPropertyChanged(nameof(MaxParallelFileSizeLimit)); SaveConfig(); }
        }

        public string MaxParallelFileSizeLimitUnit
        {
            get => CurrentSettings.MaxParallelFileSizeLimitUnit;
            set { CurrentSettings.MaxParallelFileSizeLimitUnit = value; OnPropertyChanged(nameof(MaxParallelFileSizeLimitUnit)); SaveConfig(); }
        }

        public string EncryptionKey
        {
            get => CurrentSettings.EncryptionKey;
            set { CurrentSettings.EncryptionKey = value; OnPropertyChanged(nameof(EncryptionKey)); SaveConfig(); }
        }

        private string _newSoftware;
        public string NewSoftware
        {
            get => _newSoftware;
            set { _newSoftware = value; OnPropertyChanged(nameof(NewSoftware)); }
        }

        private string _selectedSoftware;
        public string SelectedSoftware
        {
            get => _selectedSoftware;
            set { _selectedSoftware = value; OnPropertyChanged(nameof(SelectedSoftware)); }
        }

        public string EncryptedExtensionsString
        {
            get => string.Join(";", CurrentSettings.EncryptedExtensions);
            set
            {
                CurrentSettings.EncryptedExtensions = value.Split(';', System.StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
                OnPropertyChanged(nameof(EncryptedExtensionsString));
                SaveConfig();
            }
        }

        public string PriorityExtensionsString
        {
            get => string.Join(";", CurrentSettings.PriorityExtensions);
            set
            {
                CurrentSettings.PriorityExtensions = value.Split(';', System.StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
                OnPropertyChanged(nameof(PriorityExtensionsString));
                SaveConfig();
            }
        }

        public ICommand ChangeLanguageCommand { get; }
        public ICommand AddSoftwareCommand { get; }
        public ICommand RemoveSoftwareCommand { get; }

        public SettingsViewModel(SettingService configManager, LanguageService languageService, JobManagementViewModel jobVM, NetworkService networkService, SynchronizationContext syncContext)
        {
            _configManager = configManager;
            LanguageService = languageService;
            _jobVM = jobVM;
            _networkService = networkService;
            _syncContext = syncContext;

            CurrentSettings = _configManager.LoadSettings();
            Softwares = new ObservableCollection<string>(CurrentSettings.BusinessSoftwares);

            ChangeLanguageCommand = new RelayViewModel(ExecuteChangeLanguage);
            AddSoftwareCommand = new RelayViewModel(ExecuteAddSoftware);
            RemoveSoftwareCommand = new RelayViewModel(ExecuteRemoveSoftware);

            LanguageService.CurrentLanguage = CurrentSettings.Language;

            if (_networkService != null)
            {
                _networkService.OnConnectionStatusChanged += HandleConnectionStatusChanged;
            }

            LanguageService.PropertyChanged += (s, e) => { if (e.PropertyName == "Item[]") UpdateConnectionStatusUI(); };
            ConnectionStatusText = LanguageService["StatusDisconnected"];
        }

        private void HandleConnectionStatusChanged(ConnectionStatus status)
        {
            _currentConnectionStatus = status;
            UpdateConnectionStatusUI();
        }

        private void UpdateConnectionStatusUI()
        {
            Action action = () =>
            {
                if (_currentConnectionStatus == ConnectionStatus.Connected) { ConnectionStatusText = LanguageService["StatusConnected"]; ConnectionStatusColor = "#2ECC71"; }
                else if (_currentConnectionStatus == ConnectionStatus.Connecting) { ConnectionStatusText = LanguageService["StatusConnecting"]; ConnectionStatusColor = "#F39C12"; }
                else { ConnectionStatusText = LanguageService["StatusDisconnected"]; ConnectionStatusColor = "#E74C3C"; }
            };
            if (_syncContext != null) _syncContext.Post(_ => action(), null);
            else action();
        }

        private void ExecuteChangeLanguage(object param)
        {
            if (param is string lang) Language = lang;
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
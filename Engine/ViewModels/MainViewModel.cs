using EasySave.Services;
using System.ComponentModel;
using System.Text.Json;
using System.Windows.Input;

namespace EasySave.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly SettingService _configManager;
        private readonly BackupService _backupEngine;
        private readonly StateService _stateTracker;
        private readonly NetworkService _networkService;
        private readonly SynchronizationContext _syncContext;

        public LanguageService LanguageService { get; }
        public JobManagementViewModel JobVM { get; }
        public SettingsViewModel SettingsVM { get; }
        public NetworkViewModel NetworkVM { get; }

        private bool _isSettingsOpen = false;
        public bool IsSettingsOpen { get => _isSettingsOpen; set { _isSettingsOpen = value; OnPropertyChanged(nameof(IsSettingsOpen)); } }

        public ICommand ToggleSettingsCommand { get; }

        public MainViewModel(SettingService configManager, StateService stateTracker, BackupService backupEngine, NetworkService networkService)
        {
            _configManager = configManager;
            _stateTracker = stateTracker;
            _backupEngine = backupEngine;
            _networkService = networkService;
            _syncContext = SynchronizationContext.Current;

            LanguageService = new LanguageService();

            JobVM = new JobManagementViewModel(_configManager, _backupEngine, _networkService, LanguageService, _syncContext);
            SettingsVM = new SettingsViewModel(_configManager, LanguageService, JobVM);
            NetworkVM = new NetworkViewModel(_networkService, _stateTracker, _configManager, LanguageService, _syncContext);

            NetworkVM.OnNewRemoteActivity += (msg) => { JobVM.ExternalActivityUpdate(msg); };

            ToggleSettingsCommand = new RelayViewModel(p => IsSettingsOpen = !IsSettingsOpen);

            EasyLog.DailyLogger.OnLogGenerated += (jobId, format, entry) =>
            {
                try { string jsonLog = JsonSerializer.Serialize(entry); _networkService.SendMessage($"[LOG]|{jobId}|{format}|{jsonLog}"); }
                catch { }
            };

            EasySave.Services.StateService.OnStateUpdated += (jsonState) => { _networkService.SendMessage($"[STATE]|{jsonState}"); };
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
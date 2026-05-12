using EasySave.Models;
using EasySave.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace EasySave.ViewModels
{
    public class JobManagementViewModel : INotifyPropertyChanged
    {
        private readonly ConfigManager _configManager;
        private readonly BackupEngine _backupEngine;
        private readonly NetworkService _networkService;
        private readonly SynchronizationContext _uiContext;

        public LanguageService LanguageService { get; }
        public ObservableCollection<JobViewModel> Jobs { get; }
        public List<BackupType> AvailableTypes { get; } = new List<BackupType> { BackupType.Full, BackupType.Differential };
        public bool IsJobSelected => SelectedJob != null;

        private JobViewModel _selectedJob;
        public JobViewModel SelectedJob { get => _selectedJob; set { if (_selectedJob != value) { _selectedJob = value; OnPropertyChanged(nameof(SelectedJob)); OnPropertyChanged(nameof(IsJobSelected)); } } }

        private string _currentFile;
        public string CurrentFile { get => _currentFile; private set { if (_currentFile != value) { _currentFile = value; OnPropertyChanged(nameof(CurrentFile)); } } }

        public ICommand AddJobCommand { get; }
        public ICommand ExecuteSelectionCommand { get; }
        public ICommand DeleteJobCommand { get; }
        public ICommand PauseJobCommand { get; }
        public ICommand ResumeJobCommand { get; }
        public ICommand StopJobCommand { get; }

        public JobManagementViewModel(ConfigManager configManager, BackupEngine backupEngine, NetworkService networkService, LanguageService languageService, SynchronizationContext uiContext)
        {
            _configManager = configManager;
            _backupEngine = backupEngine;
            _networkService = networkService;
            LanguageService = languageService;
            _uiContext = uiContext;

            var settings = _configManager.LoadSettings();
            Jobs = new ObservableCollection<JobViewModel>(settings.Jobs.Select(j => new JobViewModel(j)));
            foreach (var job in Jobs) job.PropertyChanged += OnJobPropertyChanged;

            AddJobCommand = new RelayCommand(ExecuteAddJob);
            ExecuteSelectionCommand = new RelayCommand(ExecuteSelectedJob);
            DeleteJobCommand = new RelayCommand(ExecuteDeleteJob);
            PauseJobCommand = new RelayCommand(ExecutePauseJob);
            ResumeJobCommand = new RelayCommand(ExecuteResumeJob);
            StopJobCommand = new RelayCommand(ExecuteStopJob);

            RegisterEngineEvents();
        }

        public void ExternalActivityUpdate(string message)
        {
            string displayMsg = message;

            if (message.Contains(": START")) displayMsg = message.Replace(": START", " : " + LanguageService["StateActive"]);
            else if (message.Contains(": END")) displayMsg = message.Replace(": END", " : " + LanguageService["StateFinished"]);

            if (_uiContext != null) _uiContext.Post(_ => CurrentFile = displayMsg, null);
            else CurrentFile = displayMsg;
        }

        private void UpdateActivityBar(string rawMessage)
        {
            string displayMsg = rawMessage;

            if (rawMessage.Contains(": START")) displayMsg = rawMessage.Replace(": START", " : " + LanguageService["StateActive"]);
            else if (rawMessage.Contains(": END")) displayMsg = rawMessage.Replace(": END", " : " + LanguageService["StateFinished"]);

            if (_uiContext != null) _uiContext.Post(_ => CurrentFile = displayMsg, null);
            else CurrentFile = displayMsg;

            if (rawMessage.Contains(": START")) _networkService.SendMessage($"[START] {rawMessage.Split(':')[0].Trim()}");
            else if (rawMessage.Contains(": END")) _networkService.SendMessage($"[END] {rawMessage.Split(':')[0].Trim()}");
            else if (rawMessage.Contains(": ERREUR") || rawMessage.Contains(": ERROR")) _networkService.SendMessage($"[ERROR] {rawMessage}");
            else _networkService.SendMessage($"[PROGRESS] {rawMessage}");
        }

        private void RegisterEngineEvents()
        {
            _backupEngine.OnProgressUpdate += (file, remaining) => { };

            _backupEngine.OnActivityMessage += (message) => { UpdateActivityBar(message); };

            _backupEngine.OnJobSuspendedBySoftware += (jobName, isSuspended, software) =>
            {
                Action action = () => { foreach (var jobVm in Jobs.Where(j => j.IsRunning)) { jobVm.IsSoftwareSuspended = isSuspended; } };
                if (_uiContext != null) _uiContext.Post(_ => action(), null); else action();
            };

            _backupEngine.OnJobWaiting += (jobName, isWaiting) =>
            {
                Action action = () => { var jobVm = Jobs.FirstOrDefault(j => j.Name == jobName); if (jobVm != null) jobVm.IsWaiting = isWaiting; };
                if (_uiContext != null) _uiContext.Post(_ => action(), null); else action();
            };

            _backupEngine.OnJobBlocked += (jobName, isBlocked) =>
            {
                Action action = () => { var jobVm = Jobs.FirstOrDefault(j => j.Name == jobName); if (jobVm != null) jobVm.IsBlocked = isBlocked; };
                if (_uiContext != null) _uiContext.Post(_ => action(), null); else action();
            };

            _backupEngine.OnJobProgress += (jobName, progress, speed) =>
            {
                Action action = () =>
                {
                    var jobVm = Jobs.FirstOrDefault(j => j.Name == jobName);
                    if (jobVm != null)
                    {
                        jobVm.Progress = progress;
                        jobVm.CurrentSpeed = speed;
                    }
                };
                if (_uiContext != null) _uiContext.Post(_ => action(), null); else action();
            };
        }

        private void OnJobPropertyChanged(object sender, PropertyChangedEventArgs e) { if (e.PropertyName == "Progress" || e.PropertyName == "CurrentSpeed" || e.PropertyName == "IsSelected" || e.PropertyName == "IsRunning" || e.PropertyName == "IsPaused" || e.PropertyName == "IsWaiting" || e.PropertyName == "IsBlocked" || e.PropertyName == "IsSoftwareSuspended" || e.PropertyName == "IsFinished" || e.PropertyName == "IsCanceled") return; SaveConfig(); }

        public void SaveConfig() { var settings = _configManager.LoadSettings(); settings.Jobs = Jobs.Select(j => j.Model).ToList(); _configManager.SaveSettings(settings); }

        private async void ExecuteSelectedJob(object parameter)
        {
            var jobsToRun = Jobs.Where(j => j.IsSelected).ToList();
            if (!jobsToRun.Any()) { CurrentFile = LanguageService["MsgEmptyPath"]; return; }
            try
            {
                CurrentFile = LanguageService["MsgStartGlobal"];
                foreach (var jobVm in jobsToRun)
                {
                    if (string.IsNullOrWhiteSpace(jobVm.SourceDirectory) || string.IsNullOrWhiteSpace(jobVm.TargetDirectory)) continue;
                    jobVm.Progress = 0; jobVm.CurrentSpeed = ""; jobVm.IsRunning = true; jobVm.IsFinished = false; jobVm.IsCanceled = false; jobVm.IsPaused = false; jobVm.IsSoftwareSuspended = _backupEngine.GetRunningBusinessSoftware() != null;
                    _ = Task.Run(async () =>
                    {
                        bool wasCanceled = false;
                        try { await _backupEngine.ExecuteJobAsync(jobVm.Model); }
                        catch (Exception ex)
                        {
                            if (ex.Message == "Job stopped manually.") wasCanceled = true;
                            string msg = wasCanceled ? LanguageService["MsgJobStopped"] : $"{LanguageService["MsgError"]} {ex.Message}";
                            if (_uiContext != null) _uiContext.Post(_ => CurrentFile = msg, null); else CurrentFile = msg;
                        }
                        finally
                        {
                            Action reset = () =>
                            {
                                jobVm.IsWaiting = false;
                                jobVm.IsBlocked = false;
                                jobVm.IsPaused = false;
                                jobVm.IsSoftwareSuspended = false;
                                jobVm.IsRunning = false;
                                jobVm.CurrentSpeed = "";

                                if (wasCanceled)
                                {
                                    jobVm.IsCanceled = true;
                                    Task.Run(async () =>
                                    {
                                        await Task.Delay(1500);
                                        Action finalize = () => { jobVm.IsCanceled = false; jobVm.Progress = 0; };
                                        if (_uiContext != null) _uiContext.Post(_ => finalize(), null); else finalize();
                                    });
                                }
                                else if (jobVm.Progress >= 100)
                                {
                                    jobVm.IsFinished = true;
                                    Task.Run(async () =>
                                    {
                                        await Task.Delay(1500);
                                        Action finalize = () => { jobVm.IsFinished = false; jobVm.Progress = 0; };
                                        if (_uiContext != null) _uiContext.Post(_ => finalize(), null); else finalize();
                                    });
                                }
                                else { jobVm.Progress = 0; }
                            };
                            if (_uiContext != null) _uiContext.Post(_ => reset(), null); else reset();
                        }
                    });
                }
            }
            catch (Exception ex) { CurrentFile = $"{LanguageService["MsgError"]} {ex.Message}"; }
        }

        public async Task ExecuteJobsAsync(List<int> ids)
        {
            try
            {
                CurrentFile = LanguageService["MsgStartGlobal"];
                foreach (var id in ids)
                {
                    var jobVm = Jobs.FirstOrDefault(j => j.Id == id);
                    if (jobVm == null || string.IsNullOrWhiteSpace(jobVm.SourceDirectory) || string.IsNullOrWhiteSpace(jobVm.TargetDirectory)) continue;
                    jobVm.Progress = 0; jobVm.CurrentSpeed = ""; jobVm.IsRunning = true; jobVm.IsFinished = false; jobVm.IsCanceled = false; jobVm.IsPaused = false; jobVm.IsSoftwareSuspended = _backupEngine.GetRunningBusinessSoftware() != null;

                    bool wasCanceled = false;
                    try { await _backupEngine.ExecuteJobAsync(jobVm.Model); }
                    catch (Exception ex)
                    {
                        if (ex.Message == "Job stopped manually.") wasCanceled = true;
                        string msg = wasCanceled ? LanguageService["MsgJobStopped"] : $"{LanguageService["MsgError"]} {ex.Message}";
                        CurrentFile = msg;
                    }
                    finally
                    {
                        jobVm.IsWaiting = false;
                        jobVm.IsBlocked = false;
                        jobVm.IsPaused = false;
                        jobVm.IsSoftwareSuspended = false;
                        jobVm.IsRunning = false;
                        jobVm.CurrentSpeed = "";

                        if (wasCanceled)
                        {
                            jobVm.IsCanceled = true;
                            Task.Run(async () =>
                            {
                                await Task.Delay(1500);
                                Action finalize = () => { jobVm.IsCanceled = false; jobVm.Progress = 0; };
                                if (_uiContext != null) _uiContext.Post(_ => finalize(), null); else finalize();
                            });
                        }
                        else if (jobVm.Progress >= 100)
                        {
                            jobVm.IsFinished = true;
                            Task.Run(async () =>
                            {
                                await Task.Delay(1500);
                                Action finalize = () => { jobVm.IsFinished = false; jobVm.Progress = 0; };
                                if (_uiContext != null) _uiContext.Post(_ => finalize(), null); else finalize();
                            });
                        }
                        else { jobVm.Progress = 0; }
                    }
                }
            }
            catch (Exception ex) { CurrentFile = $"{LanguageService["MsgError"]} {ex.Message}"; }
        }

        private void ExecutePauseJob(object parameter) { if (parameter is JobViewModel job) { _backupEngine.PauseJob(job.Name); job.IsPaused = true; UpdateActivityBar($"{job.Name} : PAUSE MANUELLE"); } }
        private void ExecuteResumeJob(object parameter) { if (parameter is JobViewModel job) { if (_backupEngine.GetRunningBusinessSoftware() != null) { CurrentFile = LanguageService["MsgBlockingSoftware"]; return; } _backupEngine.ResumeJob(job.Name); job.IsPaused = false; UpdateActivityBar($"{job.Name} : REPRISE"); } }
        private void ExecuteStopJob(object parameter) { if (parameter is JobViewModel job) { _backupEngine.StopJob(job.Name); UpdateActivityBar($"{job.Name} : ARRÊT FORCÉ"); } }

        private void ExecuteAddJob(object parameter) { int newId = Jobs.Count > 0 ? Jobs.Max(j => j.Id) + 1 : 1; var newViewModel = new JobViewModel(new BackupJob { Id = newId, Name = $"Save {newId}", SourceDirectory = "", TargetDirectory = "", Type = BackupType.Full }); newViewModel.PropertyChanged += OnJobPropertyChanged; Jobs.Add(newViewModel); SaveConfig(); SelectedJob = newViewModel; CurrentFile = LanguageService["MsgSlotAdded"]; }

        private void ExecuteDeleteJob(object parameter)
        {
            var jobsToDelete = Jobs.Where(j => j.IsSelected).ToList();

            if (jobsToDelete.Any())
            {
                foreach (var job in jobsToDelete)
                {
                    job.PropertyChanged -= OnJobPropertyChanged;
                    Jobs.Remove(job);
                }
                SaveConfig();
                CurrentFile = LanguageService["MsgDeleted"];
                SelectedJob = null;
            }
            else if (SelectedJob != null)
            {
                SelectedJob.PropertyChanged -= OnJobPropertyChanged;
                Jobs.Remove(SelectedJob);
                SaveConfig();
                CurrentFile = LanguageService["MsgDeleted"];
                SelectedJob = null;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
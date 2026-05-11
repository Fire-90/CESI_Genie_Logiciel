using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using EasySave.ViewModels;
using EasySave.Services;
using EasySave.Models;

namespace Graphic
{
    public partial class MainWindow : Window
    {
        private MainViewModel _mainViewModel;

        public MainWindow()
        {
            InitializeComponent();

            var configManager = new ConfigManager();
            var appSettings = configManager.LoadSettings();
            var stateTracker = new StateTracker(appSettings.Jobs);
            var engine = new BackupEngine(stateTracker, configManager);

            var networkService = new NetworkService(configManager);

            _mainViewModel = new MainViewModel(configManager, stateTracker, engine, networkService);
            this.DataContext = _mainViewModel;

            this.Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            string[] args = Environment.GetCommandLineArgs();

            if (args.Length > 1)
            {
                string inputArgs = args[1];
                List<int> idsToExecute = ParseArgs(inputArgs);

                if (idsToExecute.Count > 0)
                {
                    await _mainViewModel.ExecuteJobsAsync(idsToExecute);
                }
            }
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl tabControl)
            {
                // L'index 1 correspond à l'onglet "Processus"
                if (tabControl.SelectedIndex == 1)
                {
                    _mainViewModel?.RefreshProcessesCommand.Execute(null);
                }
            }
        }

        private List<int> ParseArgs(string arg)
        {
            var ids = new List<int>();

            if (arg.Contains("-"))
            {
                var p = arg.Split('-');
                if (int.TryParse(p[0], out int s) && int.TryParse(p[1], out int e))
                {
                    for (int i = s; i <= e; i++)
                        ids.Add(i);
                }
            }
            else if (arg.Contains(";"))
            {
                foreach (var p in arg.Split(';'))
                {
                    if (int.TryParse(p, out int id))
                        ids.Add(id);
                }
            }
            else if (int.TryParse(arg, out int id))
            {
                ids.Add(id);
            }

            return ids;
        }
    }
}
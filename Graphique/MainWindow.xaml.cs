using System;
using System.Collections.Generic;
using System.Windows;
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

            // 1. Initialisation des services
            var configManager = new ConfigManager();
            var jobs = configManager.LoadConfig();
            var stateTracker = new StateTracker(jobs);
            var engine = new BackupEngine(stateTracker);

            // 2. Création du ViewModel
            _mainViewModel = new MainViewModel(configManager, stateTracker, engine);
            this.DataContext = _mainViewModel;

            // 3. On demande à vérifier les arguments du terminal UNE FOIS que la fenêtre est chargée
            this.Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Récupère tout ce qui a été tapé dans le terminal
            string[] args = Environment.GetCommandLineArgs();

            // args[0] est toujours le chemin "Graphic.exe"
            // args[1] sera ton "1-2" ou "1;3"
            if (args.Length > 1)
            {
                string inputArgs = args[1];
                List<int> idsToExecute = ParseArgs(inputArgs);

                if (idsToExecute.Count > 0)
                {
                    // Lancement automatique !
                    await _mainViewModel.ExecuteJobsAsync(idsToExecute);
                }
            }
        }

        // Ton ancienne méthode de parsing récupérée de la Console !
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
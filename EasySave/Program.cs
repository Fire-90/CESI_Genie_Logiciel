using EasySave.Core;
using EasySave.Models;
using EasySave.ViewModels;
using EasySave.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EasySave.ConsoleApp
{
    class Program
    {
        private static ConsoleView _view;
        private static BackupEngine _engine;
        private static ConfigManager _configManager;
        private static StateTracker _stateTracker;
        private static List<BackupJob> _jobs;

        static async Task Main(string[] args)
        {
            _view = new ConsoleView();

            string lang = _view.ChooseLanguage();
            LanguageManager.SetLanguage(lang);

            _configManager = new ConfigManager();
            _jobs = _configManager.LoadConfig();
            _stateTracker = new StateTracker(_jobs);
            _engine = new BackupEngine(_stateTracker);

            _engine.OnProgressUpdate += (file, remaining) =>
                _view.DisplayMessage("Copying", file);

            if (args.Length > 0)
            {
                await ExecuteJobsAsync(ParseArgs(args[0]));
            }
            else
            {
                await RunMenuLoopAsync();
            }
        }

        private static async Task RunMenuLoopAsync()
        {
            bool exit = false;
            while (!exit)
            {
                _view.DisplayMenu(_jobs);
                string input = _view.ReadInput();

                if (string.IsNullOrWhiteSpace(input)) continue;

                if (input.Trim().Equals("Q", StringComparison.OrdinalIgnoreCase))
                {
                    exit = true;
                }
                else
                {
                    List<int> idsToExecute = ParseArgs(input);

                    if (idsToExecute.Count == 0)
                    {
                        _view.DisplayMessage("InvalidInput");
                    }
                    else
                    {
                        await ExecuteJobsAsync(idsToExecute);
                    }

                    _view.WaitForAcknowledge();
                }
            }
        }

        private static async Task ExecuteJobsAsync(List<int> ids)
        {
            foreach (var id in ids)
            {
                var job = _jobs.FirstOrDefault(j => j.Id == id);

                if (job == null)
                {
                    _view.DisplayMessage("JobNotFound", id);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(job.SourceDirectory))
                {
                    _view.DisplayMessage("SlotEmpty", job.Id);
                    string oldName = job.Name;

                    _view.ConfigureJob(job);
                    _configManager.SaveConfig(_jobs);
                    _stateTracker.UpdateJobName(oldName, job.Name);

                    // Feedback : Confirmation de la configuration
                    _view.DisplayMessage("ConfigSuccess", job.Name);
                }

                _view.DisplayMessage("JobStart", job.Name);
                try
                {
                    await _engine.ExecuteJobAsync(job);
                    _view.DisplayMessage("JobEnd", job.Name);
                }
                catch (Exception ex)
                {
                    _view.DisplayMessage("JobError", job.Name, ex.Message);
                }
            }
        }

        private static List<int> ParseArgs(string arg)
        {
            var ids = new List<int>();
            if (arg.Contains("-"))
            {
                var p = arg.Split('-');
                if (int.TryParse(p[0], out int s) && int.TryParse(p[1], out int e))
                    for (int i = s; i <= e; i++) ids.Add(i);
            }
            else if (arg.Contains(";"))
            {
                foreach (var p in arg.Split(';')) if (int.TryParse(p, out int id)) ids.Add(id);
            }
            else if (int.TryParse(arg, out int id)) ids.Add(id);
            return ids;
        }
    }
}
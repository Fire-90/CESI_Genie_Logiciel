using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EasySave.Models;
using EasySave.ViewModels;
using EasySave.Views;

namespace EasySave.ConsoleApp
{
    class Program
    {
        private static List<BackupJob> _jobs;
        private static BackupEngine _engine;
        private static ConfigManager _configManager;
        private static StateTracker _stateTracker;

        static async Task Main(string[] args)
        {
            // Étape 1 : Choix obligatoire de la langue au démarrage
            ChooseLanguage();

            InitializeApplication();

            if (args.Length > 0)
            {
                await ExecuteJobsAsync(args[0]);
            }
            else
            {
                await RunInteractiveMenuAsync();
            }
        }

        private static void ChooseLanguage()
        {
            string choice = "";
            while (choice != "1" && choice != "2")
            {
                Console.Clear();
                Console.WriteLine("==================================");
                Console.WriteLine("        LANGUAGE / LANGUE         ");
                Console.WriteLine("==================================");
                Console.WriteLine(" 1 - English (EN)");
                Console.WriteLine(" 2 - Français (FR)");
                Console.Write("\n Choose / Choisissez (1/2) : ");

                choice = Console.ReadLine()?.Trim();
            }

            if (choice == "2")
            {
                LanguageManager.SetLanguage("FR");
            }
            else
            {
                LanguageManager.SetLanguage("EN");
            }

            Console.Clear(); // Nettoie la console avant d'afficher le menu principal
        }

        private static void InitializeApplication()
        {
            _configManager = new ConfigManager();
            _jobs = _configManager.LoadConfig();

            _stateTracker = new StateTracker(_jobs);
            _engine = new BackupEngine(_stateTracker);

            _engine.OnProgressUpdate += (file, remaining) =>
            {
                Console.WriteLine($"{LanguageManager.GetString("Copying")}{file}");
            };
        }

        private static async Task RunInteractiveMenuAsync()
        {
            bool exit = false;
            while (!exit)
            {
                Console.WriteLine("\n==================================");
                Console.WriteLine(LanguageManager.GetString("MenuTitle"));
                Console.WriteLine("==================================");

                foreach (var job in _jobs)
                {
                    string status = string.IsNullOrWhiteSpace(job.SourceDirectory)
                        ? LanguageManager.GetString("Empty")
                        : LanguageManager.GetString("Ready");

                    Console.WriteLine($" [{job.Id}] {job.Name} {status} ({job.Type})");
                    if (!string.IsNullOrWhiteSpace(job.SourceDirectory))
                    {
                        Console.WriteLine($"{LanguageManager.GetString("Source")}{job.SourceDirectory}");
                        Console.WriteLine($"{LanguageManager.GetString("Target")}{job.TargetDirectory}");
                    }
                }

                Console.WriteLine(LanguageManager.GetString("OptionsTitle"));
                Console.WriteLine(LanguageManager.GetString("OptionExecute"));
                Console.WriteLine(LanguageManager.GetString("OptionQuit"));
                Console.Write(LanguageManager.GetString("YourChoice"));

                string input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input)) continue;

                if (input.Trim().Equals("Q", StringComparison.OrdinalIgnoreCase))
                {
                    exit = true;
                }
                else
                {
                    await ExecuteJobsAsync(input);
                }
            }
        }

        private static async Task ExecuteJobsAsync(string inputArguments)
        {
            List<int> jobIdsToExecute = ParseCommandLineArgs(inputArguments);

            if (jobIdsToExecute.Count == 0) return;

            foreach (var id in jobIdsToExecute)
            {
                var job = _jobs.FirstOrDefault(j => j.Id == id);
                if (job != null)
                {
                    if (string.IsNullOrWhiteSpace(job.SourceDirectory))
                    {
                        Console.WriteLine(string.Format(LanguageManager.GetString("SlotEmpty"), job.Id));
                        string oldName = job.Name;

                        ConfigureJobInteractively(job);

                        _configManager.SaveConfig(_jobs);
                        _stateTracker.UpdateJobName(oldName, job.Name);
                    }

                    Console.WriteLine(string.Format(LanguageManager.GetString("JobStart"), job.Name));
                    try
                    {
                        await _engine.ExecuteJobAsync(job);
                        Console.WriteLine(string.Format(LanguageManager.GetString("JobEnd"), job.Name));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(string.Format(LanguageManager.GetString("JobError"), job.Name, ex.Message));
                    }
                }
            }
        }

        private static void ConfigureJobInteractively(BackupJob job)
        {
            Console.Write(LanguageManager.GetString("AskName"));
            string nameInput = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(nameInput)) job.Name = nameInput;

            Console.Write(LanguageManager.GetString("AskSource"));
            job.SourceDirectory = Console.ReadLine();

            Console.Write(LanguageManager.GetString("AskTarget"));
            job.TargetDirectory = Console.ReadLine();

            Console.Write(LanguageManager.GetString("AskType"));
            string typeInput = Console.ReadLine();
            job.Type = (typeInput == "2") ? BackupType.Differential : BackupType.Full;
        }

        private static List<int> ParseCommandLineArgs(string arg)
        {
            var ids = new List<int>();
            if (arg.Contains("-")) { var parts = arg.Split('-'); if (parts.Length == 2 && int.TryParse(parts[0], out int s) && int.TryParse(parts[1], out int e)) for (int i = s; i <= e; i++) ids.Add(i); }
            else if (arg.Contains(";")) { var parts = arg.Split(';'); foreach (var p in parts) if (int.TryParse(p, out int id)) ids.Add(id); }
            else if (int.TryParse(arg, out int id)) ids.Add(id);
            return ids;
        }
    }
}
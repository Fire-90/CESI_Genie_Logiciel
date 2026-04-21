using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EasySave.Models;
using EasySave.Core;

namespace EasySave.ConsoleApp
{
    class Program
    {
        private static List<BackupJob> _jobs;
        private static BackupEngine _engine;
        private static ConfigManager _configManager;
        private static StateTracker _stateTracker; // Rendu accessible globalement

        static async Task Main(string[] args)
        {
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

        private static void InitializeApplication()
        {
            _configManager = new ConfigManager();
            _jobs = _configManager.LoadConfig();

            _stateTracker = new StateTracker(_jobs);
            _engine = new BackupEngine(_stateTracker);

            _engine.OnProgressUpdate += (file, remaining) =>
            {
                Console.WriteLine($"   Copie en cours : {file}");
            };
        }

        private static async Task RunInteractiveMenuAsync()
        {
            bool exit = false;
            while (!exit)
            {
                Console.WriteLine("\n==================================");
                Console.WriteLine("        EASY SAVE - MENU");
                Console.WriteLine("==================================");

                foreach (var job in _jobs)
                {
                    string status = string.IsNullOrWhiteSpace(job.SourceDirectory) ? "[VIDE]" : "[PRÊT]";
                    Console.WriteLine($" [{job.Id}] {job.Name} {status} ({job.Type})");
                    if (!string.IsNullOrWhiteSpace(job.SourceDirectory))
                    {
                        Console.WriteLine($"     Source : {job.SourceDirectory}");
                        Console.WriteLine($"     Cible  : {job.TargetDirectory}");
                    }
                }

                Console.WriteLine("\nOptions :");
                Console.WriteLine(" - Saisissez les ID à exécuter (ex: 1, 1-3, 1;3)");
                Console.WriteLine(" - Saisissez 'Q' pour quitter");
                Console.Write("\nVotre choix : ");

                string input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input)) continue;

                if (input.Trim().Equals("Q", StringComparison.OrdinalIgnoreCase)) exit = true;
                else await ExecuteJobsAsync(input);
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
                    // VERIFICATION : Si le slot est vide, on demande la configuration
                    if (string.IsNullOrWhiteSpace(job.SourceDirectory))
                    {
                        Console.WriteLine($"\n[INFO] L'emplacement [{job.Id}] est vide. Configuration requise.");
                        string oldName = job.Name;

                        ConfigureJobInteractively(job);

                        // Sauvegarde immédiate dans data/config.json
                        _configManager.SaveConfig(_jobs);

                        // Met à jour le nom dans data/state.json
                        _stateTracker.UpdateJobName(oldName, job.Name);
                    }

                    Console.WriteLine($"\n>>> Démarrage du travail : {job.Name} <<<");
                    try
                    {
                        await _engine.ExecuteJobAsync(job);
                        Console.WriteLine($">>> Fin du travail : {job.Name} <<<");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Erreur] Échec de {job.Name} : {ex.Message}");
                    }
                }
            }
        }

        // Nouvelle méthode de saisie utilisateur
        private static void ConfigureJobInteractively(BackupJob job)
        {
            Console.Write(" -> Nom de la sauvegarde : ");
            string nameInput = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(nameInput)) job.Name = nameInput;

            Console.Write(" -> Répertoire source complet (ex: C:\\Source) : ");
            job.SourceDirectory = Console.ReadLine();

            Console.Write(" -> Répertoire cible complet (ex: D:\\Backup) : ");
            job.TargetDirectory = Console.ReadLine();

            Console.Write(" -> Type (1 = Complet, 2 = Différentiel) : ");
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
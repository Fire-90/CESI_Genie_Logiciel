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
        // Déclaration globale pour un accès depuis les différentes méthodes
        private static List<BackupJob> _jobs;
        private static BackupEngine _engine;

        static async Task Main(string[] args)
        {
            InitializeApplication();

            // S'il y a des arguments, on exécute directement (Mode CLI)
            if (args.Length > 0)
            {
                await ExecuteJobsAsync(args[0]);
            }
            // Sinon, on lance le mode interactif (Menu)
            else
            {
                await RunInteractiveMenuAsync();
            }
        }

        private static void InitializeApplication()
        {
            // Initialisation d'une liste de 5 travaux avec les chemins spécifiques
            _jobs = new List<BackupJob>
            {
                new BackupJob(1, "Job1", @"C:\Users\Fire\Documents\Repos\Source1", @"C:\Users\Fire\Documents\Repos\Backup1", BackupType.Full),
                new BackupJob(2, "Job2", @"C:\Users\Fire\Documents\Repos\Source2", @"C:\Users\Fire\Documents\Repos\Backup2", BackupType.Differential),
                new BackupJob(3, "Job3", @"C:\Users\Fire\Documents\Repos\Source3", @"C:\Users\Fire\Documents\Repos\Backup3", BackupType.Full),
                new BackupJob(4, "Job4", @"C:\Users\Fire\Documents\Repos\Source4", @"C:\Users\Fire\Documents\Repos\Backup4", BackupType.Differential),
                new BackupJob(5, "Job5", @"C:\Users\Fire\Documents\Repos\Source5", @"C:\Users\Fire\Documents\Repos\Backup5", BackupType.Full)
            };

            _engine = new BackupEngine();

            // Abonnement à l'événement pour affichage console
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
                Console.WriteLine("Travaux de sauvegarde configurés :");

                foreach (var job in _jobs)
                {
                    Console.WriteLine($" [{job.Id}] {job.Name} ({job.Type})");
                    Console.WriteLine($"     Source : {job.SourceDirectory}");
                    Console.WriteLine($"     Cible  : {job.TargetDirectory}");
                }

                Console.WriteLine("\nOptions :");
                Console.WriteLine(" - Saisissez les ID à exécuter (ex: 1, 1-3, 1;3)");
                Console.WriteLine(" - Saisissez 'Q' pour quitter");
                Console.Write("\nVotre choix : ");

                string input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input))
                {
                    continue;
                }

                if (input.Trim().Equals("Q", StringComparison.OrdinalIgnoreCase))
                {
                    exit = true;
                    Console.WriteLine("Fermeture de l'application...");
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

            if (jobIdsToExecute.Count == 0)
            {
                Console.WriteLine("\n[Erreur] Aucune tâche correspondante trouvée pour cette saisie.");
                return;
            }

            foreach (var id in jobIdsToExecute)
            {
                var job = _jobs.FirstOrDefault(j => j.Id == id);
                if (job != null)
                {
                    Console.WriteLine($"\n>>> Démarrage du travail : {job.Name} <<<");
                    try
                    {
                        await _engine.ExecuteJobAsync(job);
                        Console.WriteLine($">>> Fin du travail : {job.Name} avec succès <<<");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Erreur] Échec du travail {job.Name} : {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"[Avertissement] Le travail avec l'ID {id} n'existe pas.");
                }
            }
        }

        private static List<int> ParseCommandLineArgs(string arg)
        {
            var ids = new List<int>();

            // Gestion du format "1-3"
            if (arg.Contains("-"))
            {
                var parts = arg.Split('-');
                if (parts.Length == 2 && int.TryParse(parts[0], out int start) && int.TryParse(parts[1], out int end))
                {
                    for (int i = start; i <= end; i++) ids.Add(i);
                }
            }
            // Gestion du format "1;3"
            else if (arg.Contains(";"))
            {
                var parts = arg.Split(';');
                foreach (var part in parts)
                {
                    if (int.TryParse(part, out int id)) ids.Add(id);
                }
            }
            // Cas d'un ID unique "1"
            else if (int.TryParse(arg, out int id))
            {
                ids.Add(id);
            }

            return ids;
        }
    }
}
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
        static async Task Main(string[] args)
        {
            // Initialisation d'une liste de travaux simulée (à remplacer par la lecture d'un fichier JSON de configuration)
            var jobs = new List<BackupJob>
            {
                new BackupJob(1, "Job1", @"C:\Source1", @"D:\Backup1", BackupType.Full),
                new BackupJob(2, "Job2", @"C:\Source2", @"D:\Backup2", BackupType.Differential),
                new BackupJob(3, "Job3", @"C:\Source3", @"D:\Backup3", BackupType.Full)
            };

            if (args.Length > 0)
            {
                List<int> jobIdsToExecute = ParseCommandLineArgs(args[0]);
                var engine = new BackupEngine();

                // Abonnement à l'événement pour affichage console
                engine.OnProgressUpdate += (file, remaining) =>
                {
                    Console.WriteLine($"Copying: {file}");
                };

                foreach (var id in jobIdsToExecute)
                {
                    var job = jobs.FirstOrDefault(j => j.Id == id);
                    if (job != null)
                    {
                        Console.WriteLine($"\n--- Starting Job: {job.Name} ---");
                        await engine.ExecuteJobAsync(job);
                        Console.WriteLine($"--- Finished Job: {job.Name} ---");
                    }
                    else
                    {
                        Console.WriteLine($"Job ID {id} not found.");
                    }
                }
            }
            else
            {
                Console.WriteLine("EasySave v1.0");
                Console.WriteLine("Usage: EasySave.exe <1-3> or <1;3>");
                // Ici, lancer le mode interactif (menus) si aucun argument n'est fourni
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
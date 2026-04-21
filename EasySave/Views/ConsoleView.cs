using System;
using System.Collections.Generic;
using EasySave.Models;
using EasySave.Core;

namespace EasySave.Views
{
    public class ConsoleView
    {
        public string ChooseLanguage()
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
            return choice == "2" ? "FR" : "EN";
        }

        public void DisplayMenu(List<BackupJob> jobs)
        {
            Console.Clear();
            Console.WriteLine("==================================");
            Console.WriteLine(LanguageManager.GetString("MenuTitle"));
            Console.WriteLine("==================================");

            foreach (var job in jobs)
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
        }

        public void DisplayMessage(string key, params object[] args)
        {
            string message = LanguageManager.GetString(key);
            Console.WriteLine(args.Length > 0 ? string.Format(message, args) : message);
        }

        public string ReadInput() => Console.ReadLine();

        public void WaitForAcknowledge()
        {
            Console.WriteLine(LanguageManager.GetString("PressAnyKey"));
            Console.ReadKey();
        }

        public void ConfigureJob(BackupJob job)
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
    }
}
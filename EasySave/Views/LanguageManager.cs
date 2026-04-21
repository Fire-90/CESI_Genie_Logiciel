using System;
using System.Collections.Generic;

namespace EasySave.Views
{
    public static class LanguageManager
    {
        public static string CurrentLanguage { get; private set; } = "EN"; // Anglais par défaut

        public static void SetLanguage(string languageCode)
        {
            if (languageCode == "EN" || languageCode == "FR")
            {
                CurrentLanguage = languageCode;
            }
        }

        private static readonly Dictionary<string, Dictionary<string, string>> Translations = new Dictionary<string, Dictionary<string, string>>
        {
            {
                "EN", new Dictionary<string, string>
                {
                    { "MenuTitle", "        EASY SAVE - MENU" },
                    { "Copying", "   Copying : " },
                    { "Empty", "[EMPTY]" },
                    { "Ready", "[READY]" },
                    { "Source", "     Source : " },
                    { "Target", "     Target : " },
                    { "OptionsTitle", "\nOptions :" },
                    { "OptionExecute", " - Enter IDs to execute (e.g., 1, 1-3, 1;3)" },
                    { "OptionQuit", " - Enter 'Q' to quit" },
                    { "YourChoice", "\nYour choice : " },
                    { "SlotEmpty", "\n[INFO] Slot [{0}] is empty. Configuration required." },
                    { "AskName", " -> Backup name : " },
                    { "AskSource", " -> Full source directory (e.g., C:\\Source) : " },
                    { "AskTarget", " -> Full target directory (e.g., D:\\Backup) : " },
                    { "AskType", " -> Type (1 = Full, 2 = Differential) : " },
                    { "JobStart", "\n>>> Starting job : {0} <<<" },
                    { "JobEnd", ">>> Job finished : {0} <<<" },
                    { "JobError", "[Error] Job {0} failed : {1}" }
                }
            },
            {
                "FR", new Dictionary<string, string>
                {
                    { "MenuTitle", "        EASY SAVE - MENU" },
                    { "Copying", "   Copie en cours : " },
                    { "Empty", "[VIDE]" },
                    { "Ready", "[PRÊT]" },
                    { "Source", "     Source : " },
                    { "Target", "     Cible  : " },
                    { "OptionsTitle", "\nOptions :" },
                    { "OptionExecute", " - Saisissez les ID à exécuter (ex: 1, 1-3, 1;3)" },
                    { "OptionQuit", " - Saisissez 'Q' pour quitter" },
                    { "YourChoice", "\nVotre choix : " },
                    { "SlotEmpty", "\n[INFO] L'emplacement [{0}] est vide. Configuration requise." },
                    { "AskName", " -> Nom de la sauvegarde : " },
                    { "AskSource", " -> Répertoire source complet (ex: C:\\Source) : " },
                    { "AskTarget", " -> Répertoire cible complet (ex: D:\\Backup) : " },
                    { "AskType", " -> Type (1 = Complet, 2 = Différentiel) : " },
                    { "JobStart", "\n>>> Démarrage du travail : {0} <<<" },
                    { "JobEnd", ">>> Fin du travail : {0} <<<" },
                    { "JobError", "[Erreur] Échec de {0} : {1}" }
                }
            }
        };

        public static string GetString(string key)
        {
            if (Translations.ContainsKey(CurrentLanguage) && Translations[CurrentLanguage].ContainsKey(key))
            {
                return Translations[CurrentLanguage][key];
            }
            return key;
        }
    }
}
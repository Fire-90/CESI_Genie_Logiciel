using System;
using System.Collections.Generic;

namespace EasySave.Core
{
    public static class LanguageManager
    {
        public static string CurrentLanguage { get; private set; } = "EN";

        public static void SetLanguage(string languageCode)
        {
            if (languageCode == "EN" || languageCode == "FR") CurrentLanguage = languageCode;
        }

        private static readonly Dictionary<string, Dictionary<string, string>> Translations = new Dictionary<string, Dictionary<string, string>>
        {
            {
                "EN", new Dictionary<string, string>
                {
                    { "MenuTitle", "        EASY SAVE - MENU" },
                    { "Copying", "   Copying : {0}" },
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
                    { "JobError", "[Error] Job {0} failed : {1}" },
                    { "InvalidInput", "\n[Error] Invalid input. Please enter a valid ID format (e.g., 1, 1-3, 1;3) or 'Q'." },
                    { "JobNotFound", "\n[Warning] No backup job found with ID {0}." },
                    { "ConfigSuccess", "[Success] Configuration saved for {0}." },
                    { "PressAnyKey", "\nPress any key to return to the menu..." }
                }
            },
            {
                "FR", new Dictionary<string, string>
                {
                    { "MenuTitle", "        EASY SAVE - MENU" },
                    { "Copying", "   Copie en cours : {0}" },
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
                    { "JobError", "[Erreur] Échec de {0} : {1}" },
                    { "InvalidInput", "\n[Erreur] Saisie invalide. Veuillez entrer un format d'ID valide (ex: 1, 1-3, 1;3) ou 'Q'." },
                    { "JobNotFound", "\n[Attention] Aucun travail de sauvegarde trouvé avec l'ID {0}." },
                    { "ConfigSuccess", "[Succès] Configuration enregistrée pour {0}." },
                    { "PressAnyKey", "\nAppuyez sur une touche pour retourner au menu..." }
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
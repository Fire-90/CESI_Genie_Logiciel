using System.Collections.Generic;
using System.ComponentModel;

namespace EasySave.Services
{
    public class LanguageService : INotifyPropertyChanged
    {
        private string _currentLanguage = "FR";

        public string CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                if (_currentLanguage != value)
                {
                    _currentLanguage = value;
                    OnPropertyChanged("Item[]");
                    OnPropertyChanged(null);
                }
            }
        }

        public string this[string key]
        {
            get
            {
                if (Translations.ContainsKey(_currentLanguage) && Translations[_currentLanguage].ContainsKey(key))
                {
                    return Translations[_currentLanguage][key];
                }
                return key;
            }
        }

        private static readonly Dictionary<string, Dictionary<string, string>> Translations = new Dictionary<string, Dictionary<string, string>>
        {
            { "EN", new Dictionary<string, string> {
                { "Title", "EasySave 2.0" },
                { "TabJobs", "Job Management" },
                { "TabProcesses", "Processes" },
                { "TabSettings", "Settings" },
                { "LblName", "Job Name" },
                { "LblSource", "Source Directory" },
                { "LblTarget", "Target Directory" },
                { "LblType", "Backup Type" },
                { "BtnAdd", "+ Add" },
                { "BtnRun", "▶ Run" },
                { "BtnDelete", "🗑 Delete" },
                { "Settings", "⚙ Settings" },
                { "Close", "Close" },
                { "SaveSettings", "Save" },
                { "Softwares", "Business Softwares:" },
                { "LblLogFormat", "Log Format:" },
                { "LblLogDestination", "Log Destination:" },
                { "LblEncryptedExt", "Encryption Extensions:" },
                { "LblPriorityExt", "Priority Extensions:" },
                { "LblServerIP", "Server IP:" },
                { "Recent", "Recent Activity:" },
                { "ProcessListTitle", "Active Processes" },
                { "BtnStopProcess", "Stop Process" },
                { "MsgEmptyPath", "❌ Error: Source or Target directory is empty." },
                { "MsgSlotAdded", "✅ New empty job added." },
                { "MsgDeleted", "🗑️ Job deleted successfully." },
                
                // New centralized messages
                { "MsgStartGlobal", "⏳ Starting background backups..." },
                { "MsgSuccessGlobal", "✅ All selected backups completed successfully!" },
                { "MsgError", "❌ Error:" },
                { "MsgBlockingSoftware", "❌ Backup paused/interrupted. Blocking software running:" }
            }},
            { "FR", new Dictionary<string, string> {
                { "Title", "EasySave 2.0" },
                { "TabJobs", "Gestion des Travaux" },
                { "TabProcesses", "Processus" },
                { "TabSettings", "Paramètres" },
                { "LblName", "Nom du travail" },
                { "LblSource", "Dossier Source" },
                { "LblTarget", "Dossier Cible" },
                { "LblType", "Type de sauvegarde" },
                { "BtnAdd", "+ Ajouter" },
                { "BtnRun", "▶ Lancer" },
                { "BtnDelete", "🗑 Supprimer" },
                { "Settings", "⚙ Paramètres" },
                { "Close", "Fermer" },
                { "SaveSettings", "Enregistrer" },
                { "Softwares", "Logiciels métiers :" },
                { "LblLogFormat", "Format des logs :" },
                { "LblLogDestination", "Destination des logs :" },
                { "LblEncryptedExt", "Extensions à chiffrer :" },
                { "LblPriorityExt", "Extensions prioritaires :" },
                { "LblServerIP", "IP du serveur :" },
                { "Recent", "Activité récente :" },
                { "ProcessListTitle", "Liste des processus actifs" },
                { "BtnStopProcess", "Arrêter le processus" },
                { "MsgEmptyPath", "❌ Erreur : Le dossier Source ou Cible est vide." },
                { "MsgSlotAdded", "✅ Nouveau travail vide ajouté." },
                { "MsgDeleted", "🗑️ Travail supprimé avec succès." },

                // Nouveaux messages centralisés
                { "MsgStartGlobal", "⏳ Démarrage des sauvegardes en arrière-plan..." },
                { "MsgSuccessGlobal", "✅ Toutes les sauvegardes sélectionnées sont terminées !" },
                { "MsgError", "❌ Erreur :" },
                { "MsgBlockingSoftware", "❌ Sauvegarde interrompue ! Logiciel métier détecté :" }
            }}
        };

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
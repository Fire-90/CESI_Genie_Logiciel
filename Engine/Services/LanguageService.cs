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
                { "Title", "EasySave 3.0" },
                { "TabJobs", "Job Management" },
                { "TabProcesses", "Processes" },
                { "TabSettings", "Settings" },
                { "LblName", "Job Name" },
                { "LblSource", "Source Directory" },
                { "LblTarget", "Target Directory" },
                { "LblType", "Backup Type" },
                { "LblStatus", "Status" },
                { "BtnAdd", "+ Add" },
                { "BtnRun", "▶ Run" },
                { "BtnDelete", "🗑 Delete" },
                { "Settings", "⚙ Settings" },
                { "Close", "Close" },
                { "SaveSettings", "Save" },
                { "Softwares", "Business Softwares:" },
                { "LblLogFormat", "Log Format:" },
                { "LblLogDestination", "Log Destination:" },
                { "LblMaxParallelSize", "Max Parallel File Size (KB):" },
                { "LblEncryptedExt", "Encryption Extensions:" },
                { "LblEncryptionKey", "Encryption Key:" },
                { "LblPriorityExt", "Priority Extensions:" },
                { "LblServerIP", "Server IP:" },
                { "Recent", "Recent Activity:" },
                { "ProcessListTitle", "Active Processes" },
                { "BtnStopProcess", "Stop Process" },
                { "MsgEmptyPath", "❌ Error: Source or Target directory is empty." },
                { "MsgSlotAdded", "✅ New empty job added." },
                { "MsgDeleted", "🗑️ Job deleted successfully." },

                { "StateInactive", "INACTIVE" },
                { "StateActive", "ACTIVE" },
                { "StateSuspended", "SUSPENDED" },
                { "StateWaiting", "WAITING" },
                { "StateStopped", "JUST STOPPED" },
                { "StateFinished", "FINISHED" },

                { "MsgStartGlobal", "⏳ Starting background backups..." },
                { "MsgSuccessGlobal", "✅ All selected backups completed successfully!" },
                { "MsgError", "❌ Error:" },
                { "MsgBlockingSoftware", "❌ Backup paused/interrupted. Blocking software running:" },
                { "MsgJobStopped", "Job stopped." },

                { "TitleGeneral", "⚙ General Settings" },
                { "LblLanguage", "Language" },
                { "TitleNetwork", "🌐 Network" },
                { "LblStatusNetwork", "Status:" },
                { "LblClientName", "Client Name:" },
                { "TitleSecurity", "🔒 Security" },
                { "LblExtensionsHelp", "Separate with semicolons (e.g., .txt;.pdf)" },
                { "TitleSoftwares", "🚫 Business Softwares" },
                { "ProcessTabTitle", "Other Users Tracking (Server)" },
                { "ProcessTabEmpty", "No other user connected or no state available." },
                { "ProcessTabRemaining", " remaining" },
                { "StatusConnected", "Connected" },
                { "StatusConnecting", "Connecting..." },
                { "StatusDisconnected", "Failed / Disconnected" }
            }},
            { "FR", new Dictionary<string, string> {
                { "Title", "EasySave 3.0" },
                { "TabJobs", "Gestion des Travaux" },
                { "TabProcesses", "Processus" },
                { "TabSettings", "Paramètres" },
                { "LblName", "Nom du travail" },
                { "LblSource", "Dossier Source" },
                { "LblTarget", "Dossier Cible" },
                { "LblType", "Type de sauvegarde" },
                { "LblStatus", "Statut" },
                { "BtnAdd", "+ Ajouter" },
                { "BtnRun", "▶ Lancer" },
                { "BtnDelete", "🗑 Supprimer" },
                { "Settings", "⚙ Paramètres" },
                { "Close", "Fermer" },
                { "SaveSettings", "Enregistrer" },
                { "Softwares", "Logiciels métiers :" },
                { "LblLogFormat", "Format des logs :" },
                { "LblLogDestination", "Destination des logs :" },
                { "LblMaxParallelSize", "Taille max transfert parallèle (Ko) :" },
                { "LblEncryptedExt", "Extensions à chiffrer :" },
                { "LblEncryptionKey", "Clé de chiffrement :" },
                { "LblPriorityExt", "Extensions prioritaires :" },
                { "LblServerIP", "IP du serveur :" },
                { "Recent", "Activité récente :" },
                { "ProcessListTitle", "Liste des processus actifs" },
                { "BtnStopProcess", "Arrêter le processus" },
                { "MsgEmptyPath", "❌ Erreur : Le dossier Source ou Cible est vide." },
                { "MsgSlotAdded", "✅ Nouveau travail vide ajouté." },
                { "MsgDeleted", "🗑️ Travail supprimé avec succès." },

                { "StateInactive", "INACTIF" },
                { "StateActive", "ACTIF" },
                { "StateSuspended", "SUSPENDU" },
                { "StateWaiting", "EN ATTENTE" },
                { "StateStopped", "ARRÊTÉ" },
                { "StateFinished", "TERMINÉ" },

                { "MsgStartGlobal", "⏳ Démarrage des sauvegardes en arrière-plan..." },
                { "MsgSuccessGlobal", "✅ Toutes les sauvegardes sélectionnées sont terminées !" },
                { "MsgError", "❌ Erreur :" },
                { "MsgBlockingSoftware", "❌ Sauvegarde interrompue ! Logiciel métier détecté :" },
                { "MsgJobStopped", "Sauvegarde interrompue." },

                { "TitleGeneral", "⚙ Paramètres Généraux" },
                { "LblLanguage", "Langue / Language" },
                { "TitleNetwork", "🌐 Réseau" },
                { "LblStatusNetwork", "Statut :" },
                { "LblClientName", "Nom du Client :" },
                { "TitleSecurity", "🔒 Sécurité" },
                { "LblExtensionsHelp", "Séparez par des points-virgules (ex: .txt;.pdf)" },
                { "TitleSoftwares", "🚫 Logiciels Métiers" },
                { "ProcessTabTitle", "Suivi des autres utilisateurs (Serveur)" },
                { "ProcessTabEmpty", "Aucun autre utilisateur connecté ou aucun état disponible." },
                { "ProcessTabRemaining", " restants" },
                { "StatusConnected", "Connecté" },
                { "StatusConnecting", "En cours..." },
                { "StatusDisconnected", "Échec / Déconnecté" }
            }}
        };

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
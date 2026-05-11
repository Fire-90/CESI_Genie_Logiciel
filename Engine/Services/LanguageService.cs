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
                    // Notifie l'interface WPF que TOUTES les valeurs liées à l'indexeur ont changé
                    OnPropertyChanged("Item[]");
                    // Force WPF à réévaluer absolument toutes les propriétés de cet objet
                    OnPropertyChanged(null);
                }
            }
        }

        // L'indexeur permet au XAML de faire {Binding UIStrings[MaCle]}
        public string this[string key]
        {
            get
            {
                if (Translations.ContainsKey(_currentLanguage) && Translations[_currentLanguage].ContainsKey(key))
                {
                    return Translations[_currentLanguage][key];
                }
                return key; // Retourne la clé elle-même si la traduction manque
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
                { "BtnAdd", "+ Add Job" },
                { "BtnRun", "▶ Run Selection" },
                { "BtnDelete", "🗑 Delete" },
                { "SaveSettings", "Save Configuration" },
                { "Softwares", "Business Softwares:" },
                { "LblLogFormat", "Log Format:" },
                { "LblEncryptedExt", "Encrypted Extensions:" },
                { "Recent", "Recent activity:" },
                // Ajouts pour l'onglet des processus
                { "ProcessListTitle", "Active Processes List" },
                { "BtnStopProcess", "Stop Process" }
            }},
            { "FR", new Dictionary<string, string> {
                { "Title", "EasySave 2.0" },
                { "TabJobs", "Gestion des Jobs" },
                { "TabProcesses", "Processus" },
                { "TabSettings", "Paramètres" },
                { "LblName", "Nom du travail" },
                { "LblSource", "Dossier Source" },
                { "LblTarget", "Dossier Cible" },
                { "LblType", "Type de sauvegarde" },
                { "BtnAdd", "+ Ajouter" },
                { "BtnRun", "▶ Lancer" },
                { "BtnDelete", "🗑 Supprimer" },
                { "SaveSettings", "Enregistrer" },
                { "Softwares", "Logiciels métiers :" },
                { "LblLogFormat", "Format des logs :" },
                { "LblEncryptedExt", "Extensions à chiffrer :" },
                { "Recent", "Activité récente :" },
                { "ProcessListTitle", "Liste des processus actifs" },
                { "BtnStopProcess", "Arrêter le processus" }
            }}
        };

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
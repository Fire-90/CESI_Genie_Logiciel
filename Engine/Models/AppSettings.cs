using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System;
using System.Collections.Generic;

namespace EasySave.Models
{
    public class AppSettings
    {
        public string Language { get; set; } = "FR";
        public string LogFormat { get; set; } = "json";
        public string LogDestination { get; set; } = "LocalAndServer";
        public string ServerIP { get; set; } = "127.0.0.1";
        public string ClientName { get; set; } = "Client-" + Environment.MachineName;

        public long MaxParallelFileSizeLimit { get; set; } = 500;
        public string MaxParallelFileSizeLimitUnit { get; set; } = "Mo";
        public string EncryptionKey { get; set; } = "EasySaveKey";

        public List<BackupJob> Jobs { get; set; } = new List<BackupJob>();
        public List<string> BusinessSoftwares { get; set; } = new List<string> { "CalculatorApp", "notepad" };
        public List<string> EncryptedExtensions { get; set; } = new List<string> { ".txt", ".docx" };
        public List<string> PriorityExtensions { get; set; } = new List<string> { ".pdf" };
    }
}
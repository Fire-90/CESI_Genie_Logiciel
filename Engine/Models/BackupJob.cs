using EasySave.Models;
using System.Text.Json.Serialization;

namespace EasySave.Models
{
    public class BackupJob
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string SourceDirectory { get; set; }
        public string TargetDirectory { get; set; }
        public BackupType Type { get; set; }

        public BackupJob() { }

        public BackupJob(int id, string name, string sourceDirectory, string targetDirectory, BackupType type)
        {
            Id = id;
            Name = name;
            SourceDirectory = sourceDirectory;
            TargetDirectory = targetDirectory;
            Type = type;
        }
    }
}
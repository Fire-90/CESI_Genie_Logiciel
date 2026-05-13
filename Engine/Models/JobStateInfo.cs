using System.Text.Json.Serialization;

namespace EasySave.Models
{
    public class JobStateInfo
    {
        [JsonPropertyName("Name")] public string Name { get; set; }
        [JsonPropertyName("SourceFilePath")] public string SourceFilePath { get; set; }
        [JsonPropertyName("TargetFilePath")] public string TargetFilePath { get; set; }
        [JsonPropertyName("State")] public string State { get; set; }
        [JsonPropertyName("TotalFilesToCopy")] public int TotalFilesToCopy { get; set; }
        [JsonPropertyName("TotalFilesSize")] public long TotalFilesSize { get; set; }
        [JsonPropertyName("NbFilesLeftToDo")] public int NbFilesLeftToDo { get; set; }
        [JsonPropertyName("Progression")] public int Progression { get; set; }
        [JsonPropertyName("CurrentSpeed")] public string CurrentSpeed { get; set; }
        [JsonPropertyName("LastActionDate")] public string LastActionDate { get; set; }
        [JsonPropertyName("RemainingFilesSize")] public long RemainingFilesSize { get; set; }
    }
}
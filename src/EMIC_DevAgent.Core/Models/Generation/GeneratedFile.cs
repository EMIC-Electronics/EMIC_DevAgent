namespace EMIC_DevAgent.Core.Models.Generation;

public class GeneratedFile
{
    public string RelativePath { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public FileType Type { get; set; }
    public string GeneratedByAgent { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

public enum FileType
{
    Emic,
    Header,
    Source,
    Json,
    Xml
}

namespace EMIC_DevAgent.Core.Models.Generation;

public class GenerationPlan
{
    public string ComponentName { get; set; } = string.Empty;
    public string ComponentType { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public List<PlannedFile> FilesToGenerate { get; } = new();
    public List<string> DependenciesToResolve { get; } = new();
    public bool RequiresProgramXml { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class PlannedFile
{
    public string RelativePath { get; set; } = string.Empty;
    public FileType Type { get; set; }
    public string Purpose { get; set; } = string.Empty;
}

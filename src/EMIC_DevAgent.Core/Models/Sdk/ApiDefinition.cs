namespace EMIC_DevAgent.Core.Models.Sdk;

public class ApiDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string EmicFilePath { get; set; } = string.Empty;
    public string IncPath { get; set; } = string.Empty;
    public string SrcPath { get; set; } = string.Empty;
    public List<string> Functions { get; } = new();
    public List<string> Dependencies { get; } = new();
    public Dictionary<string, string> Dictionaries { get; } = new();
}

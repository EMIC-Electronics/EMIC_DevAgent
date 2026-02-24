namespace EMIC_DevAgent.Core.Models.Sdk;

public class DriverDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string EmicFilePath { get; set; } = string.Empty;
    public List<string> HalDependencies { get; } = new();
    public List<string> Functions { get; } = new();
    public string ChipType { get; set; } = string.Empty;
}

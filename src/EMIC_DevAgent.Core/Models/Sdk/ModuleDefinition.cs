namespace EMIC_DevAgent.Core.Models.Sdk;

public class ModuleDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string GenerateEmicPath { get; set; } = string.Empty;
    public string DeployEmicPath { get; set; } = string.Empty;
    public string DescriptionJsonPath { get; set; } = string.Empty;
    public List<string> RequiredApis { get; } = new();
    public List<string> RequiredDrivers { get; } = new();
    public string HardwareBoard { get; set; } = string.Empty;
}

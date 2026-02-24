namespace EMIC_DevAgent.Core.Models.Sdk;

public class SdkInventory
{
    public string SdkRootPath { get; set; } = string.Empty;
    public List<ApiDefinition> Apis { get; } = new();
    public List<DriverDefinition> Drivers { get; } = new();
    public List<ModuleDefinition> Modules { get; } = new();
    public List<string> HalComponents { get; } = new();
    public DateTime LastScanTime { get; set; }
}

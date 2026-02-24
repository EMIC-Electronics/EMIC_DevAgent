using EMIC_DevAgent.Core.Models.Sdk;

namespace EMIC_DevAgent.Core.Services.Sdk;

public interface ISdkScanner
{
    Task<SdkInventory> ScanAsync(string sdkPath, CancellationToken ct = default);
    Task<ApiDefinition?> FindApiAsync(string name, CancellationToken ct = default);
    Task<DriverDefinition?> FindDriverAsync(string name, CancellationToken ct = default);
    Task<ModuleDefinition?> FindModuleAsync(string name, CancellationToken ct = default);
}

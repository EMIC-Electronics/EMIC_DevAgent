using EMIC_DevAgent.Core.Models.Sdk;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Services.Sdk;

public class SdkScanner : ISdkScanner
{
    private readonly SdkPathResolver _pathResolver;
    private readonly EmicFileParser _fileParser;
    private readonly ILogger<SdkScanner> _logger;

    public SdkScanner(SdkPathResolver pathResolver, EmicFileParser fileParser, ILogger<SdkScanner> logger)
    {
        _pathResolver = pathResolver;
        _fileParser = fileParser;
        _logger = logger;
    }

    public Task<SdkInventory> ScanAsync(string sdkPath, CancellationToken ct = default)
    {
        throw new NotImplementedException("SdkScanner.ScanAsync pendiente de implementacion");
    }

    public Task<ApiDefinition?> FindApiAsync(string name, CancellationToken ct = default)
    {
        throw new NotImplementedException("SdkScanner.FindApiAsync pendiente de implementacion");
    }

    public Task<DriverDefinition?> FindDriverAsync(string name, CancellationToken ct = default)
    {
        throw new NotImplementedException("SdkScanner.FindDriverAsync pendiente de implementacion");
    }

    public Task<ModuleDefinition?> FindModuleAsync(string name, CancellationToken ct = default)
    {
        throw new NotImplementedException("SdkScanner.FindModuleAsync pendiente de implementacion");
    }
}

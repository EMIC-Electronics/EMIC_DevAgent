using EMIC.Shared.Services.Storage;
using EMIC_DevAgent.Core.Configuration;

namespace EMIC_DevAgent.Core.Services.Sdk;

public class SdkPathResolver
{
    private readonly SdkPaths _paths;
    private readonly MediaAccess _mediaAccess;

    public SdkPathResolver(SdkPaths paths, MediaAccess mediaAccess)
    {
        _paths = paths;
        _mediaAccess = mediaAccess;
    }

    public string GetApiPath() => _mediaAccess.Path.Combine(_paths.SdkRoot, "_api");
    public string GetDriversPath() => _mediaAccess.Path.Combine(_paths.SdkRoot, "_drivers");
    public string GetModulesPath() => _mediaAccess.Path.Combine(_paths.SdkRoot, "_modules");
    public string GetHalPath() => _mediaAccess.Path.Combine(_paths.SdkRoot, "_hal");
    public string GetMainPath() => _mediaAccess.Path.Combine(_paths.SdkRoot, "_main");
}

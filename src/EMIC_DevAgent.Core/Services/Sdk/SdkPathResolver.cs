using EMIC_DevAgent.Core.Configuration;

namespace EMIC_DevAgent.Core.Services.Sdk;

public class SdkPathResolver
{
    private readonly SdkPaths _paths;

    public SdkPathResolver(SdkPaths paths)
    {
        _paths = paths;
    }

    public string GetApiPath() => Path.Combine(_paths.SdkRoot, "_api");
    public string GetDriversPath() => Path.Combine(_paths.SdkRoot, "_drivers");
    public string GetModulesPath() => Path.Combine(_paths.SdkRoot, "_modules");
    public string GetHalPath() => Path.Combine(_paths.SdkRoot, "_hal");
    public string GetMainPath() => Path.Combine(_paths.SdkRoot, "_main");

    public string ResolveVolume(string emicPath)
    {
        throw new NotImplementedException("SdkPathResolver.ResolveVolume pendiente de implementacion");
    }
}

namespace EMIC_DevAgent.Core.Configuration;

public class SdkPaths
{
    public string SdkRoot { get; set; } = string.Empty;

    public string ApiRoot => Path.Combine(SdkRoot, "_api");
    public string DriversRoot => Path.Combine(SdkRoot, "_drivers");
    public string ModulesRoot => Path.Combine(SdkRoot, "_modules");
    public string HalRoot => Path.Combine(SdkRoot, "_hal");
    public string MainRoot => Path.Combine(SdkRoot, "_main");

    public static SdkPaths FromConfig(EmicAgentConfig config)
        => new() { SdkRoot = config.SdkPath };
}

namespace EMIC_DevAgent.Core.Configuration;

public class SdkPaths
{
    public string SdkRoot { get; set; } = "DEV:";

    public string ApiRoot => SdkRoot.TrimEnd('/') + "/_api";
    public string DriversRoot => SdkRoot.TrimEnd('/') + "/_drivers";
    public string ModulesRoot => SdkRoot.TrimEnd('/') + "/_modules";
    public string HalRoot => SdkRoot.TrimEnd('/') + "/_hal";
    public string MainRoot => SdkRoot.TrimEnd('/') + "/_main";

    public static SdkPaths FromConfig(EmicAgentConfig config)
        => new();
}

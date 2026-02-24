using EMIC_DevAgent.Core.Configuration;

namespace EMIC_DevAgent.Cli;

public class CliAgentSession : IAgentSession
{
    public CliAgentSession(EmicAgentConfig config)
    {
        SdkPath = config.SdkPath;
    }

    public string UserEmail => "devagent@local";
    public string SdkPath { get; }
    public Dictionary<string, string> VirtualDrivers { get; } = new();
}

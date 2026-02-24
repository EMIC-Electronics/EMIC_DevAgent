namespace EMIC_DevAgent.Core.Configuration;

public interface IAgentSession
{
    string UserEmail { get; }
    string SdkPath { get; }
    Dictionary<string, string> VirtualDrivers { get; }
}

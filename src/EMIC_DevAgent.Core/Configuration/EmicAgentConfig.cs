namespace EMIC_DevAgent.Core.Configuration;

public class EmicAgentConfig
{
    public string SdkPath { get; set; } = string.Empty;
    public int MaxCompilationRetries { get; set; } = 5;
    public string DefaultMicrocontroller { get; set; } = "pic24FJ64GA002";
    public string Language { get; set; } = "es";
    public LlmConfig Llm { get; set; } = new();
}

public class LlmConfig
{
    public string Provider { get; set; } = "Claude";
    public string Model { get; set; } = "claude-sonnet-4-20250514";
    public int MaxTokens { get; set; } = 4096;
    public double Temperature { get; set; } = 0.2;
}

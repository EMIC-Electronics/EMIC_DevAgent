namespace EMIC_DevAgent.Core.Agents.Base;

public class AgentResult
{
    public string AgentName { get; set; } = string.Empty;
    public ResultStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object> Data { get; } = new();

    public static AgentResult Success(string agentName, string message = "")
        => new() { AgentName = agentName, Status = ResultStatus.Success, Message = message };

    public static AgentResult Failure(string agentName, string message)
        => new() { AgentName = agentName, Status = ResultStatus.Failure, Message = message };

    public static AgentResult NeedsInput(string agentName, string message)
        => new() { AgentName = agentName, Status = ResultStatus.NeedsInput, Message = message };
}

public enum ResultStatus
{
    Success,
    Failure,
    NeedsInput,
    Partial
}

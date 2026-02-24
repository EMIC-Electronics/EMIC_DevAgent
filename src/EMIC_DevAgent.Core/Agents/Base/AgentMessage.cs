namespace EMIC_DevAgent.Core.Agents.Base;

public class AgentMessage
{
    public string FromAgent { get; set; } = string.Empty;
    public string ToAgent { get; set; } = string.Empty;
    public MessageType Type { get; set; }
    public string Content { get; set; } = string.Empty;
    public Dictionary<string, object> Data { get; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public enum MessageType
{
    Request,
    Response,
    Error,
    Progress,
    Question
}

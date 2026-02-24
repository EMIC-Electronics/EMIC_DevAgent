namespace EMIC_DevAgent.Core.Agents.Base;

public interface IAgent
{
    string Name { get; }
    string Description { get; }
    Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default);
    bool CanHandle(AgentContext context);
}

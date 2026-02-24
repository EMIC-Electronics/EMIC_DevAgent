using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Agents.Base;

public abstract class AgentBase : IAgent
{
    protected readonly ILogger Logger;

    protected AgentBase(ILogger logger)
    {
        Logger = logger;
    }

    public abstract string Name { get; }
    public abstract string Description { get; }

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        Logger.LogInformation("Agent {Name} starting execution", Name);
        try
        {
            var result = await ExecuteCoreAsync(context, ct);
            Logger.LogInformation("Agent {Name} completed with status {Status}", Name, result.Status);
            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Agent {Name} failed", Name);
            return AgentResult.Failure(Name, ex.Message);
        }
    }

    public abstract bool CanHandle(AgentContext context);

    protected abstract Task<AgentResult> ExecuteCoreAsync(AgentContext context, CancellationToken ct);
}

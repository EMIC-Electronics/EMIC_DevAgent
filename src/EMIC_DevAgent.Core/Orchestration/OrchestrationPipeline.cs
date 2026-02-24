using EMIC_DevAgent.Core.Agents.Base;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Orchestration;

public class OrchestrationPipeline
{
    private readonly List<PipelineStep> _steps = new();
    private readonly ILogger<OrchestrationPipeline> _logger;

    public OrchestrationPipeline(ILogger<OrchestrationPipeline> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<PipelineStep> Steps => _steps.AsReadOnly();

    public OrchestrationPipeline AddStep(string name, IAgent agent, Func<AgentContext, bool>? condition = null)
    {
        _steps.Add(new PipelineStep
        {
            Name = name,
            Agent = agent,
            Condition = condition,
            Order = _steps.Count
        });
        return this;
    }

    public Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        throw new NotImplementedException("OrchestrationPipeline.ExecuteAsync pendiente de implementacion");
    }
}

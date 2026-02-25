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

    public async Task<AgentResult> ExecuteAsync(AgentContext context, CancellationToken ct = default)
    {
        _logger.LogInformation("Pipeline starting with {StepCount} steps", _steps.Count);

        AgentResult? lastResult = null;

        foreach (var step in _steps.OrderBy(s => s.Order))
        {
            ct.ThrowIfCancellationRequested();

            // Check condition
            if (step.Condition != null && !step.Condition(context))
            {
                step.Status = StepStatus.Skipped;
                _logger.LogDebug("Step '{StepName}' skipped (condition not met)", step.Name);
                continue;
            }

            // Check if agent can handle the context
            if (!step.Agent.CanHandle(context))
            {
                step.Status = StepStatus.Skipped;
                _logger.LogDebug("Step '{StepName}' skipped (agent cannot handle context)", step.Name);
                continue;
            }

            step.Status = StepStatus.Running;
            _logger.LogInformation("Step '{StepName}' starting (agent: {AgentName})", step.Name, step.Agent.Name);

            try
            {
                lastResult = await step.Agent.ExecuteAsync(context, ct);
                step.Result = lastResult;

                if (lastResult.Status == ResultStatus.Success)
                {
                    step.Status = StepStatus.Completed;
                    _logger.LogInformation("Step '{StepName}' completed successfully", step.Name);
                }
                else if (lastResult.Status == ResultStatus.Failure)
                {
                    step.Status = StepStatus.Failed;
                    _logger.LogError("Step '{StepName}' failed: {Message}", step.Name, lastResult.Message);

                    // Stop pipeline on failure
                    return lastResult;
                }
                else if (lastResult.Status == ResultStatus.NeedsInput)
                {
                    step.Status = StepStatus.Pending;
                    _logger.LogInformation("Step '{StepName}' needs input: {Message}", step.Name, lastResult.Message);
                    return lastResult;
                }
            }
            catch (OperationCanceledException)
            {
                step.Status = StepStatus.Failed;
                throw;
            }
            catch (Exception ex)
            {
                step.Status = StepStatus.Failed;
                _logger.LogError(ex, "Step '{StepName}' threw exception", step.Name);
                return AgentResult.Failure(step.Agent.Name, $"Step '{step.Name}' failed: {ex.Message}");
            }
        }

        var completedCount = _steps.Count(s => s.Status == StepStatus.Completed);
        var skippedCount = _steps.Count(s => s.Status == StepStatus.Skipped);

        _logger.LogInformation("Pipeline finished: {Completed} completed, {Skipped} skipped",
            completedCount, skippedCount);

        return lastResult ?? AgentResult.Success("Pipeline", "Pipeline completed (no steps executed)");
    }
}

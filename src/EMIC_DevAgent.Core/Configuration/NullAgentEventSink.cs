using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Models.Generation;
using EMIC_DevAgent.Core.Services.Validation;

namespace EMIC_DevAgent.Core.Configuration;

public class NullAgentEventSink : IAgentEventSink
{
    public Task OnStepStarted(string stepName, string agentName, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task OnStepCompleted(string stepName, AgentResult result, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task OnFileGenerated(GeneratedFile file, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task OnValidationResult(ValidationResult result, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task OnCompilationResult(CompilationResult result, CancellationToken ct = default)
        => Task.CompletedTask;
}

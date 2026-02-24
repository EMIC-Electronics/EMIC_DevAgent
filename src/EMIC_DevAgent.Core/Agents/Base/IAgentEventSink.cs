using EMIC_DevAgent.Core.Models.Generation;
using EMIC_DevAgent.Core.Services.Validation;

namespace EMIC_DevAgent.Core.Agents.Base;

public interface IAgentEventSink
{
    Task OnStepStarted(string stepName, string agentName, CancellationToken ct = default);
    Task OnStepCompleted(string stepName, AgentResult result, CancellationToken ct = default);
    Task OnFileGenerated(GeneratedFile file, CancellationToken ct = default);
    Task OnValidationResult(ValidationResult result, CancellationToken ct = default);
    Task OnCompilationResult(CompilationResult result, CancellationToken ct = default);
}

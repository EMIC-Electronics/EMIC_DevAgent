using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Models.Generation;
using EMIC_DevAgent.Core.Services.Validation;

namespace EMIC_DevAgent.Cli;

public class ConsoleEventSink : IAgentEventSink
{
    public Task OnStepStarted(string stepName, string agentName, CancellationToken ct = default)
    {
        Console.WriteLine($">> [{agentName}] Iniciando: {stepName}");
        return Task.CompletedTask;
    }

    public Task OnStepCompleted(string stepName, AgentResult result, CancellationToken ct = default)
    {
        Console.WriteLine($"<< [{result.AgentName}] {stepName}: {result.Status}");
        return Task.CompletedTask;
    }

    public Task OnFileGenerated(GeneratedFile file, CancellationToken ct = default)
    {
        Console.WriteLine($"   Archivo generado: {file.RelativePath} ({file.Type})");
        return Task.CompletedTask;
    }

    public Task OnValidationResult(ValidationResult result, CancellationToken ct = default)
    {
        var status = result.Passed ? "OK" : "FALLO";
        Console.WriteLine($"   Validacion [{result.ValidatorName}]: {status} ({result.Issues.Count} issues)");
        return Task.CompletedTask;
    }

    public Task OnCompilationResult(CompilationResult result, CancellationToken ct = default)
    {
        var status = result.Success ? "OK" : "FALLO";
        Console.WriteLine($"   Compilacion intento #{result.AttemptNumber}: {status}");
        if (result.Errors.Count > 0)
        {
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"     ERROR: {error}");
            }
        }
        return Task.CompletedTask;
    }
}

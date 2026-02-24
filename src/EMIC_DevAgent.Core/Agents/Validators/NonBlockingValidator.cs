using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Services.Validation;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Agents.Validators;

/// <summary>
/// Verifica que no hay while() bloqueantes ni __delay_ms() en APIs.
/// Debe usar getSystemMilis() + state machines.
/// </summary>
public class NonBlockingValidator : IValidator
{
    private readonly ILogger<NonBlockingValidator> _logger;

    public NonBlockingValidator(ILogger<NonBlockingValidator> logger)
    {
        _logger = logger;
    }

    public string Name => "NonBlocking";
    public string Description => "No hay while() bloqueantes ni __delay_ms(). Usa getSystemMilis() + state machines";

    public Task<ValidationResult> ValidateAsync(AgentContext context, CancellationToken ct = default)
    {
        throw new NotImplementedException("NonBlockingValidator pendiente de implementacion");
    }
}

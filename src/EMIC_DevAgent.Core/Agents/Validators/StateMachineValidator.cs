using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Services.Validation;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Agents.Validators;

/// <summary>
/// Verifica que operaciones complejas usan patron switch(state)
/// con variable estatica y timeouts.
/// </summary>
public class StateMachineValidator : IValidator
{
    private readonly ILogger<StateMachineValidator> _logger;

    public StateMachineValidator(ILogger<StateMachineValidator> logger)
    {
        _logger = logger;
    }

    public string Name => "StateMachine";
    public string Description => "Operaciones complejas usan patron switch(state) con variable estatica y timeouts";

    public Task<ValidationResult> ValidateAsync(AgentContext context, CancellationToken ct = default)
    {
        throw new NotImplementedException("StateMachineValidator pendiente de implementacion");
    }
}

using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Agents.Validators;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Agents;

/// <summary>
/// Delega a 4 validadores especializados:
/// - LayerSeparationValidator
/// - NonBlockingValidator
/// - StateMachineValidator
/// - DependencyValidator
/// </summary>
public class RuleValidatorAgent : AgentBase
{
    private readonly IEnumerable<IValidator> _validators;

    public RuleValidatorAgent(IEnumerable<IValidator> validators, ILogger<RuleValidatorAgent> logger)
        : base(logger)
    {
        _validators = validators;
    }

    public override string Name => "RuleValidator";
    public override string Description => "Valida reglas EMIC: separacion de capas, no-blocking, state machines, dependencias";

    public override bool CanHandle(AgentContext context)
        => context.GeneratedFiles.Count > 0;

    protected override Task<AgentResult> ExecuteCoreAsync(AgentContext context, CancellationToken ct)
    {
        throw new NotImplementedException("RuleValidatorAgent.ExecuteCoreAsync pendiente de implementacion");
    }
}

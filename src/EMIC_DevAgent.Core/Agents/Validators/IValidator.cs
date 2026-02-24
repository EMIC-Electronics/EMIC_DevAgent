using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Services.Validation;

namespace EMIC_DevAgent.Core.Agents.Validators;

public interface IValidator
{
    string Name { get; }
    string Description { get; }
    Task<ValidationResult> ValidateAsync(AgentContext context, CancellationToken ct = default);
}

using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Services.Validation;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Agents.Validators;

/// <summary>
/// Verifica que todo EMIC:setInput referencia archivos existentes
/// y no hay dependencias circulares.
/// </summary>
public class DependencyValidator : IValidator
{
    private readonly ILogger<DependencyValidator> _logger;

    public DependencyValidator(ILogger<DependencyValidator> logger)
    {
        _logger = logger;
    }

    public string Name => "Dependency";
    public string Description => "Todo EMIC:setInput referencia archivos existentes, sin dependencias circulares";

    public Task<ValidationResult> ValidateAsync(AgentContext context, CancellationToken ct = default)
    {
        throw new NotImplementedException("DependencyValidator pendiente de implementacion");
    }
}

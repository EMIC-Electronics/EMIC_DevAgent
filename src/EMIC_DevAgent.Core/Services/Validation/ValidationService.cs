using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Agents.Validators;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Services.Validation;

public class ValidationService
{
    private readonly IEnumerable<IValidator> _validators;
    private readonly ILogger<ValidationService> _logger;

    public ValidationService(IEnumerable<IValidator> validators, ILogger<ValidationService> logger)
    {
        _validators = validators;
        _logger = logger;
    }

    public async Task<List<ValidationResult>> ValidateAllAsync(AgentContext context, CancellationToken ct = default)
    {
        throw new NotImplementedException("ValidationService.ValidateAllAsync pendiente de implementacion");
    }
}

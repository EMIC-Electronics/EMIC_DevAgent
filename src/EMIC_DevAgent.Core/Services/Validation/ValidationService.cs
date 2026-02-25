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
        var results = new List<ValidationResult>();

        foreach (var validator in _validators)
        {
            ct.ThrowIfCancellationRequested();

            _logger.LogInformation("Running validator: {Name}", validator.Name);

            try
            {
                var result = await validator.ValidateAsync(context, ct);
                results.Add(result);

                var errorCount = result.Issues.Count(i => i.Severity == IssueSeverity.Error);
                var warningCount = result.Issues.Count(i => i.Severity == IssueSeverity.Warning);
                _logger.LogInformation("Validator {Name}: {Errors} errors, {Warnings} warnings",
                    validator.Name, errorCount, warningCount);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Validator {Name} threw exception", validator.Name);
                var failResult = new ValidationResult
                {
                    ValidatorName = validator.Name,
                    Passed = false
                };
                failResult.Issues.Add(new ValidationIssue
                {
                    FilePath = string.Empty,
                    Line = 0,
                    Rule = "ValidatorException",
                    Message = $"Validator '{validator.Name}' failed: {ex.Message}",
                    Severity = IssueSeverity.Error
                });
                results.Add(failResult);
            }
        }

        return results;
    }
}

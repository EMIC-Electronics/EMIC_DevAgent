using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Agents.Validators;
using EMIC_DevAgent.Core.Services.Validation;
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

    protected override async Task<AgentResult> ExecuteCoreAsync(AgentContext context, CancellationToken ct)
    {
        int totalErrors = 0;
        int totalWarnings = 0;

        foreach (var validator in _validators)
        {
            ct.ThrowIfCancellationRequested();

            Logger.LogInformation("Running validator: {Name}", validator.Name);

            try
            {
                var result = await validator.ValidateAsync(context, ct);
                context.ValidationResults.Add(result);

                totalErrors += result.Issues.Count(i => i.Severity == IssueSeverity.Error);
                totalWarnings += result.Issues.Count(i => i.Severity == IssueSeverity.Warning);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Validator {Name} failed", validator.Name);
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
                context.ValidationResults.Add(failResult);
                totalErrors++;
            }
        }

        if (totalErrors > 0)
            return AgentResult.Failure(Name, $"Validation failed: {totalErrors} errors, {totalWarnings} warnings");

        if (totalWarnings > 0)
            return AgentResult.Success(Name, $"Validation passed with {totalWarnings} warnings");

        return AgentResult.Success(Name, "All validations passed");
    }
}

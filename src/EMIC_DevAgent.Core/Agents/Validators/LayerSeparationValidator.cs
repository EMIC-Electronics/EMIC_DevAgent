using System.Text.RegularExpressions;
using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Models.Generation;
using EMIC_DevAgent.Core.Services.Validation;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Agents.Validators;

/// <summary>
/// Verifica que las APIs no acceden registros directos (TRIS, LAT, PORT).
/// Solo deben usar HAL_GPIO_*, HAL_SPI_*, HAL_I2C_*, etc.
/// </summary>
public class LayerSeparationValidator : IValidator
{
    private readonly ILogger<LayerSeparationValidator> _logger;

    private static readonly Regex HwRegisterRegex = new(
        @"\b(TRIS[A-Z]|LAT[A-Z]|PORT[A-Z]|AD1CON\d|T\dCON|U\dMODE|SPI\dCON|I2C\dCON|IFS\d|IEC\d|OSCCON)\b",
        RegexOptions.Compiled);

    private static readonly Regex HalCallRegex = new(
        @"HAL_\w+",
        RegexOptions.Compiled);

    public LayerSeparationValidator(ILogger<LayerSeparationValidator> logger)
    {
        _logger = logger;
    }

    public string Name => "LayerSeparation";
    public string Description => "APIs no acceden registros directos. Solo usan HAL_GPIO_*, HAL_SPI_*, etc.";

    public Task<ValidationResult> ValidateAsync(AgentContext context, CancellationToken ct = default)
    {
        var result = new ValidationResult
        {
            ValidatorName = Name,
            Passed = true
        };

        var apiFiles = context.GeneratedFiles
            .Where(f => f.RelativePath.Contains("_api/") &&
                        !f.RelativePath.Contains("_hard/") &&
                        !f.RelativePath.Contains("_hal/") &&
                        (f.Type == FileType.Source || f.Type == FileType.Header))
            .ToList();

        _logger.LogDebug("LayerSeparation scanning {Count} API files", apiFiles.Count);

        foreach (var file in apiFiles)
        {
            var lines = file.Content.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                ct.ThrowIfCancellationRequested();

                var line = lines[i];

                if (HalCallRegex.IsMatch(line))
                    continue;

                var matches = HwRegisterRegex.Matches(line);
                foreach (Match match in matches)
                {
                    result.Passed = false;
                    result.Issues.Add(new ValidationIssue
                    {
                        FilePath = file.RelativePath,
                        Line = i + 1,
                        Rule = "LayerSeparation",
                        Message = $"Direct hardware register access '{match.Value}' in API layer. Use HAL_* functions instead.",
                        Severity = IssueSeverity.Error
                    });
                }
            }
        }

        _logger.LogInformation("LayerSeparation: {IssueCount} issues found", result.Issues.Count);
        return Task.FromResult(result);
    }
}

using System.Text.RegularExpressions;
using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Models.Generation;
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

    private static readonly Regex DelayRegex = new(
        @"__delay_(ms|us)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex InfiniteForRegex = new(
        @"for\s*\(\s*;\s*;\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex InfiniteWhileRegex = new(
        @"while\s*\(\s*(1|true)\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex WhileRegex = new(
        @"while\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex MainFunctionRegex = new(
        @"(void|int)\s+main\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex BreakReturnTimeoutRegex = new(
        @"\b(break|return|timeout|getSystemMilis)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public NonBlockingValidator(ILogger<NonBlockingValidator> logger)
    {
        _logger = logger;
    }

    public string Name => "NonBlocking";
    public string Description => "No hay while() bloqueantes ni __delay_ms(). Usa getSystemMilis() + state machines";

    public Task<ValidationResult> ValidateAsync(AgentContext context, CancellationToken ct = default)
    {
        var result = new ValidationResult
        {
            ValidatorName = Name,
            Passed = true
        };

        var sourceFiles = context.GeneratedFiles
            .Where(f => f.Type == FileType.Source)
            .ToList();

        _logger.LogDebug("NonBlocking scanning {Count} source files", sourceFiles.Count);

        foreach (var file in sourceFiles)
        {
            ct.ThrowIfCancellationRequested();

            var lines = file.Content.Split('\n');
            bool insideMain = false;
            int braceDepth = 0;
            int mainBraceDepth = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // Track main() function scope
                if (MainFunctionRegex.IsMatch(line))
                {
                    insideMain = true;
                    mainBraceDepth = braceDepth;
                }

                braceDepth += line.Count(c => c == '{') - line.Count(c => c == '}');

                if (insideMain && braceDepth <= mainBraceDepth)
                    insideMain = false;

                // Rule 1: NoDelayMs — always an error
                if (DelayRegex.IsMatch(line))
                {
                    result.Passed = false;
                    result.Issues.Add(new ValidationIssue
                    {
                        FilePath = file.RelativePath,
                        Line = i + 1,
                        Rule = "NoDelayMs",
                        Message = "Blocking __delay_ms()/__delay_us() call. Use getSystemMilis() + state machine instead.",
                        Severity = IssueSeverity.Error
                    });
                }

                // Rule 2: NoInfiniteLoop — outside main()
                if (!insideMain && (InfiniteForRegex.IsMatch(line) || InfiniteWhileRegex.IsMatch(line)))
                {
                    result.Passed = false;
                    result.Issues.Add(new ValidationIssue
                    {
                        FilePath = file.RelativePath,
                        Line = i + 1,
                        Rule = "NoInfiniteLoop",
                        Message = "Infinite loop outside main(). Only main() should have the super-loop.",
                        Severity = IssueSeverity.Error
                    });
                }

                // Rule 3: PotentialBlockingWhile — outside main, non-infinite while without break/return/timeout in 5 lines
                if (!insideMain && WhileRegex.IsMatch(line) && !InfiniteWhileRegex.IsMatch(line))
                {
                    bool hasExit = false;
                    int lookAhead = Math.Min(5, lines.Length - i - 1);
                    for (int j = 1; j <= lookAhead; j++)
                    {
                        if (BreakReturnTimeoutRegex.IsMatch(lines[i + j]))
                        {
                            hasExit = true;
                            break;
                        }
                    }

                    if (!hasExit)
                    {
                        result.Issues.Add(new ValidationIssue
                        {
                            FilePath = file.RelativePath,
                            Line = i + 1,
                            Rule = "PotentialBlockingWhile",
                            Message = "while() loop without break/return/timeout within 5 lines. Consider adding a timeout.",
                            Severity = IssueSeverity.Warning
                        });
                    }
                }
            }
        }

        _logger.LogInformation("NonBlocking: {IssueCount} issues found", result.Issues.Count);
        return Task.FromResult(result);
    }
}

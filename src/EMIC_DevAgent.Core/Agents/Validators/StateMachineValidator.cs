using System.Text.RegularExpressions;
using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Models.Generation;
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

    private static readonly Regex FunctionSignatureRegex = new(
        @"^(?:static\s+)?(?:void|int|uint\d+_t|char|unsigned\s+\w+)\s+(\w+)\s*\([^)]*\)\s*\{?\s*$",
        RegexOptions.Compiled);

    private static readonly Regex TimingRegex = new(
        @"\b(getSystemMilis|timer|poll|tick)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SwitchRegex = new(
        @"switch\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex StateSwitchRegex = new(
        @"switch\s*\(\s*(\w+)\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex StaticVarRegex = new(
        @"static\s+\w+\s+",
        RegexOptions.Compiled);

    private static readonly Regex SkipFunctionRegex = new(
        @"^(main|.*_init|init_.*)$",
        RegexOptions.Compiled);

    public StateMachineValidator(ILogger<StateMachineValidator> logger)
    {
        _logger = logger;
    }

    public string Name => "StateMachine";
    public string Description => "Operaciones complejas usan patron switch(state) con variable estatica y timeouts";

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

        _logger.LogDebug("StateMachine scanning {Count} source files", sourceFiles.Count);

        foreach (var file in sourceFiles)
        {
            ct.ThrowIfCancellationRequested();

            var functions = ExtractFunctions(file.Content);

            foreach (var func in functions)
            {
                if (SkipFunctionRegex.IsMatch(func.Name))
                    continue;

                // Rule 1: Long function with timing but no switch
                if (func.LineCount > 20 && TimingRegex.IsMatch(func.Body) && !SwitchRegex.IsMatch(func.Body))
                {
                    result.Issues.Add(new ValidationIssue
                    {
                        FilePath = file.RelativePath,
                        Line = func.StartLine,
                        Rule = "ConsiderStateMachine",
                        Message = $"Function '{func.Name}' ({func.LineCount} lines) uses timing but no switch(state). Consider state machine pattern.",
                        Severity = IssueSeverity.Warning
                    });
                }

                // Rule 2: switch on state variable that's not static
                var switchMatches = StateSwitchRegex.Matches(func.Body);
                foreach (Match switchMatch in switchMatches)
                {
                    var stateVar = switchMatch.Groups[1].Value;
                    if (stateVar.Contains("state", StringComparison.OrdinalIgnoreCase))
                    {
                        var staticPattern = new Regex($@"static\s+\w+\s+{Regex.Escape(stateVar)}\b");
                        if (!staticPattern.IsMatch(func.Body) && !staticPattern.IsMatch(file.Content))
                        {
                            result.Issues.Add(new ValidationIssue
                            {
                                FilePath = file.RelativePath,
                                Line = func.StartLine,
                                Rule = "StaticStateVariable",
                                Message = $"State variable '{stateVar}' in function '{func.Name}' should be declared as static to preserve state between calls.",
                                Severity = IssueSeverity.Warning
                            });
                        }
                    }
                }
            }
        }

        _logger.LogInformation("StateMachine: {IssueCount} issues found", result.Issues.Count);
        return Task.FromResult(result);
    }

    private static List<FunctionInfo> ExtractFunctions(string source)
    {
        var functions = new List<FunctionInfo>();
        var lines = source.Split('\n');
        int braceDepth = 0;
        FunctionInfo? current = null;
        var bodyLines = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (current == null)
            {
                var match = FunctionSignatureRegex.Match(line.TrimStart());
                if (match.Success)
                {
                    current = new FunctionInfo
                    {
                        Name = match.Groups[1].Value,
                        StartLine = i + 1
                    };
                    braceDepth = 0;
                    bodyLines.Clear();
                }
            }

            if (current != null)
            {
                bodyLines.Add(line);
                braceDepth += line.Count(c => c == '{') - line.Count(c => c == '}');

                if (braceDepth <= 0 && bodyLines.Count > 1)
                {
                    current.Body = string.Join('\n', bodyLines);
                    current.LineCount = bodyLines.Count;
                    functions.Add(current);
                    current = null;
                }
            }
        }

        return functions;
    }

    private class FunctionInfo
    {
        public string Name { get; set; } = string.Empty;
        public int StartLine { get; set; }
        public int LineCount { get; set; }
        public string Body { get; set; } = string.Empty;
    }
}

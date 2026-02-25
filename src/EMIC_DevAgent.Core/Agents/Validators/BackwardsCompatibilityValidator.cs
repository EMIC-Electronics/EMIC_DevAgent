using System.Text.RegularExpressions;
using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Models.Generation;
using EMIC_DevAgent.Core.Services.Validation;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Agents.Validators;

/// <summary>
/// Verifica que el codigo generado usa guards condicionales para funcionalidad nueva,
/// asegurando que modulos existentes no se rompan al agregar features.
/// </summary>
public class BackwardsCompatibilityValidator : IValidator
{
    private readonly ILogger<BackwardsCompatibilityValidator> _logger;

    // Detects function declarations/definitions that are NOT wrapped in EMIC:ifdef
    private static readonly Regex FunctionDefRegex = new(
        @"^(?:void|uint\d+_t|int\d*_t|char|int|float|double)\s+(\w+)\s*\(",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // EMIC:ifdef / EMIC:endif blocks
    private static readonly Regex EmicIfdefRegex = new(
        @"EMIC:ifdef\(([^)]+)\)",
        RegexOptions.Compiled);

    private static readonly Regex EmicEndifRegex = new(
        @"EMIC:endif",
        RegexOptions.Compiled);

    // C preprocessor #ifdef / #endif
    private static readonly Regex CIfdefRegex = new(
        @"#ifdef\s+(\w+)",
        RegexOptions.Compiled);

    private static readonly Regex CEndifRegex = new(
        @"#endif",
        RegexOptions.Compiled);

    // Functions that must always exist (never need ifdef guard)
    private static readonly HashSet<string> CoreFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "init", "poll", "main"
    };

    public BackwardsCompatibilityValidator(ILogger<BackwardsCompatibilityValidator> logger)
    {
        _logger = logger;
    }

    public string Name => "BackwardsCompatibility";
    public string Description => "Verifica EMIC:ifdef/#ifdef guards para funcionalidad opcional";

    public Task<ValidationResult> ValidateAsync(AgentContext context, CancellationToken ct = default)
    {
        var result = new ValidationResult
        {
            ValidatorName = Name,
            Passed = true
        };

        foreach (var file in context.GeneratedFiles)
        {
            ct.ThrowIfCancellationRequested();

            switch (file.Type)
            {
                case FileType.Emic:
                    ValidateEmicFile(file, result);
                    break;
                case FileType.Header:
                    ValidateHeaderFile(file, result);
                    break;
                case FileType.Source:
                    ValidateSourceFile(file, result);
                    break;
            }
        }

        _logger.LogInformation("BackwardsCompatibility: {IssueCount} issues found", result.Issues.Count);
        return Task.FromResult(result);
    }

    /// <summary>
    /// Validates .emic files: optional features should use EMIC:ifdef guards.
    /// Checks that EMIC:define for non-core registrations has corresponding ifdef.
    /// </summary>
    private void ValidateEmicFile(GeneratedFile file, ValidationResult result)
    {
        var lines = file.Content.Split('\n');
        var definedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ifdefNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // Collect EMIC:ifdef names
            var ifdefMatch = EmicIfdefRegex.Match(line);
            if (ifdefMatch.Success)
                ifdefNames.Add(ifdefMatch.Groups[1].Value);

            // Collect EMIC:define names
            var defineMatch = Regex.Match(line, @"EMIC:define\(([^,)]+)");
            if (defineMatch.Success)
                definedNames.Add(defineMatch.Groups[1].Value);
        }

        // Check that events/polls definitions have ifdef guards
        foreach (var name in definedNames)
        {
            // Core registrations (inits, c_modules, main_includes) are always required
            if (name.StartsWith("inits.", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("c_modules.", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("main_includes.", StringComparison.OrdinalIgnoreCase))
                continue;

            // Events and polls should be conditional
            if (name.StartsWith("events.", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("polls.", StringComparison.OrdinalIgnoreCase))
            {
                // Extract the component part after the prefix for ifdef check
                var parts = name.Split('.');
                if (parts.Length >= 2)
                {
                    var componentKey = $"{parts[0]}.{parts[1]}";
                    if (!ifdefNames.Any(n => n.Contains(parts[1], StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Issues.Add(new ValidationIssue
                        {
                            FilePath = file.RelativePath,
                            Line = 0,
                            Rule = "OptionalDefineWithoutGuard",
                            Message = $"EMIC:define({name}) should be wrapped in EMIC:ifdef for backwards compatibility.",
                            Severity = IssueSeverity.Warning
                        });
                    }
                }
            }
        }
    }

    /// <summary>
    /// Validates .h files: non-core function declarations should be wrapped in EMIC:ifdef or #ifdef.
    /// </summary>
    private void ValidateHeaderFile(GeneratedFile file, ValidationResult result)
    {
        var lines = file.Content.Split('\n');
        int ifdefDepth = 0; // Combined EMIC:ifdef + #ifdef depth

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // Track ifdef depth
            if (EmicIfdefRegex.IsMatch(line) || CIfdefRegex.IsMatch(line))
            {
                ifdefDepth++;
                continue;
            }
            if ((EmicEndifRegex.IsMatch(line) || CEndifRegex.IsMatch(line)) && ifdefDepth > 0)
            {
                ifdefDepth--;
                continue;
            }

            // Skip include guards (#ifndef _XXX_H_ ... #endif)
            if (line.StartsWith("#ifndef") || line.StartsWith("#define _") || line.StartsWith("#define __"))
                continue;

            // Check function declarations outside of ifdef blocks
            if (ifdefDepth == 0)
            {
                var funcMatch = FunctionDefRegex.Match(line);
                if (funcMatch.Success)
                {
                    var funcName = funcMatch.Groups[1].Value;

                    // Skip core functions (always required)
                    if (IsCoreFunction(funcName))
                        continue;

                    result.Issues.Add(new ValidationIssue
                    {
                        FilePath = file.RelativePath,
                        Line = i + 1,
                        Rule = "UnguardedFunctionDeclaration",
                        Message = $"Function '{funcName}' declared outside EMIC:ifdef/endif guard. " +
                                  "Optional functions should be conditional for backwards compatibility.",
                        Severity = IssueSeverity.Warning
                    });
                }
            }
        }
    }

    /// <summary>
    /// Validates .c files: non-core function definitions should be wrapped in EMIC:ifdef or #ifdef.
    /// </summary>
    private void ValidateSourceFile(GeneratedFile file, ValidationResult result)
    {
        var lines = file.Content.Split('\n');
        int ifdefDepth = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            if (EmicIfdefRegex.IsMatch(line) || CIfdefRegex.IsMatch(line))
            {
                ifdefDepth++;
                continue;
            }
            if ((EmicEndifRegex.IsMatch(line) || CEndifRegex.IsMatch(line)) && ifdefDepth > 0)
            {
                ifdefDepth--;
                continue;
            }

            // Check function definitions outside of ifdef blocks
            if (ifdefDepth == 0)
            {
                var funcMatch = FunctionDefRegex.Match(line);
                if (funcMatch.Success && line.Contains('('))
                {
                    var funcName = funcMatch.Groups[1].Value;

                    if (IsCoreFunction(funcName))
                        continue;

                    result.Issues.Add(new ValidationIssue
                    {
                        FilePath = file.RelativePath,
                        Line = i + 1,
                        Rule = "UnguardedFunctionDefinition",
                        Message = $"Function '{funcName}' defined outside EMIC:ifdef/endif guard. " +
                                  "Optional functions should be conditional for backwards compatibility.",
                        Severity = IssueSeverity.Warning
                    });
                }
            }
        }
    }

    private static bool IsCoreFunction(string funcName)
    {
        // Exact matches
        if (CoreFunctions.Contains(funcName))
            return true;

        // Pattern matches: xxx_init, init_xxx, xxx_poll, poll_xxx
        if (funcName.EndsWith("_init", StringComparison.OrdinalIgnoreCase) ||
            funcName.StartsWith("init_", StringComparison.OrdinalIgnoreCase) ||
            funcName.EndsWith("_poll", StringComparison.OrdinalIgnoreCase) ||
            funcName.StartsWith("poll_", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}

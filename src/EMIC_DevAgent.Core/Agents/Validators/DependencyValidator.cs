using System.Text.RegularExpressions;
using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Models.Generation;
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

    private static readonly Regex SetInputRegex = new(
        @"EMIC:setInput\(([^)]+)\)",
        RegexOptions.Compiled);

    private static readonly string[] AlwaysValidPrefixes =
    {
        "SYS:", "TARGET:", "DEV:_main/", "DEV:_pcb/", "DEV:_templates/"
    };

    public DependencyValidator(ILogger<DependencyValidator> logger)
    {
        _logger = logger;
    }

    public string Name => "Dependency";
    public string Description => "Todo EMIC:setInput referencia archivos existentes, sin dependencias circulares";

    public Task<ValidationResult> ValidateAsync(AgentContext context, CancellationToken ct = default)
    {
        var result = new ValidationResult
        {
            ValidatorName = Name,
            Passed = true
        };

        var emicFiles = context.GeneratedFiles
            .Where(f => f.Type == FileType.Emic)
            .ToList();

        _logger.LogDebug("Dependency scanning {Count} emic files", emicFiles.Count);

        // Build a set of known generated file paths for reference checking
        var generatedPaths = new HashSet<string>(
            context.GeneratedFiles.Select(f => f.RelativePath),
            StringComparer.OrdinalIgnoreCase);

        // Build SDK known paths from SdkState if available
        var sdkPaths = BuildSdkPaths(context);

        // Build dependency graph: file -> list of dependencies
        var graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in emicFiles)
        {
            ct.ThrowIfCancellationRequested();

            var dependencies = new List<string>();
            var matches = SetInputRegex.Matches(file.Content);

            foreach (Match match in matches)
            {
                var depPath = match.Groups[1].Value.Trim();
                dependencies.Add(depPath);

                // Validate the dependency exists
                if (!IsAlwaysValid(depPath))
                {
                    var normalizedDep = NormalizePath(depPath);
                    bool existsInGenerated = generatedPaths.Any(p =>
                        p.Equals(normalizedDep, StringComparison.OrdinalIgnoreCase) ||
                        normalizedDep.EndsWith(p, StringComparison.OrdinalIgnoreCase) ||
                        p.EndsWith(normalizedDep, StringComparison.OrdinalIgnoreCase));

                    if (!existsInGenerated)
                    {
                        if (sdkPaths == null)
                        {
                            // No SDK state available — warn, don't error
                            result.Issues.Add(new ValidationIssue
                            {
                                FilePath = file.RelativePath,
                                Line = GetLineNumber(file.Content, match.Index),
                                Rule = "DependencyUnverified",
                                Message = $"Cannot verify dependency '{depPath}' — SDK state not available.",
                                Severity = IssueSeverity.Warning
                            });
                        }
                        else if (!sdkPaths.Contains(normalizedDep))
                        {
                            result.Passed = false;
                            result.Issues.Add(new ValidationIssue
                            {
                                FilePath = file.RelativePath,
                                Line = GetLineNumber(file.Content, match.Index),
                                Rule = "DependencyMissing",
                                Message = $"EMIC:setInput references '{depPath}' which was not found in generated files or SDK.",
                                Severity = IssueSeverity.Error
                            });
                        }
                    }
                }
            }

            graph[file.RelativePath] = dependencies;
        }

        // Detect cycles using DFS
        DetectCycles(graph, result);

        _logger.LogInformation("Dependency: {IssueCount} issues found", result.Issues.Count);
        return Task.FromResult(result);
    }

    private static bool IsAlwaysValid(string path)
    {
        return AlwaysValidPrefixes.Any(prefix =>
            path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePath(string path)
    {
        // Strip DEV: prefix for comparison with RelativePaths
        if (path.StartsWith("DEV:", StringComparison.OrdinalIgnoreCase))
            return path.Substring(4).TrimStart('/');
        return path;
    }

    private static HashSet<string>? BuildSdkPaths(AgentContext context)
    {
        if (context.SdkState == null)
            return null;

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var api in context.SdkState.Apis)
            paths.Add($"_api/{api.Category}/{api.Name}/{api.Name}.emic");

        foreach (var driver in context.SdkState.Drivers)
            paths.Add($"_drivers/{driver.Category}/{driver.Name}/{driver.Name}.emic");

        foreach (var hal in context.SdkState.HalComponents)
            paths.Add($"_hal/{hal}/{hal.ToLowerInvariant()}.emic");

        return paths;
    }

    private static int GetLineNumber(string content, int charIndex)
    {
        int line = 1;
        for (int i = 0; i < charIndex && i < content.Length; i++)
        {
            if (content[i] == '\n') line++;
        }
        return line;
    }

    private void DetectCycles(Dictionary<string, List<string>> graph, ValidationResult result)
    {
        var state = new Dictionary<string, NodeState>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();

        foreach (var node in graph.Keys)
            state[node] = NodeState.Unvisited;

        foreach (var node in graph.Keys)
        {
            if (state[node] == NodeState.Unvisited)
            {
                if (DfsCycleDetect(node, graph, state, path, result))
                    break; // Report first cycle found
            }
        }
    }

    private bool DfsCycleDetect(string node, Dictionary<string, List<string>> graph,
        Dictionary<string, NodeState> state, List<string> path, ValidationResult result)
    {
        state[node] = NodeState.InProgress;
        path.Add(node);

        if (graph.TryGetValue(node, out var deps))
        {
            foreach (var dep in deps)
            {
                var normalizedDep = NormalizePath(dep);

                // Only check cycle against known graph nodes
                if (!graph.ContainsKey(normalizedDep))
                    continue;

                if (state.TryGetValue(normalizedDep, out var depState))
                {
                    if (depState == NodeState.InProgress)
                    {
                        // Cycle found
                        var cycleStart = path.IndexOf(normalizedDep);
                        var cyclePath = path.Skip(cycleStart).Append(normalizedDep).ToList();
                        var cycleStr = string.Join(" -> ", cyclePath);

                        result.Passed = false;
                        result.Issues.Add(new ValidationIssue
                        {
                            FilePath = node,
                            Line = 0,
                            Rule = "DependencyCycle",
                            Message = $"Circular dependency detected: {cycleStr}",
                            Severity = IssueSeverity.Error
                        });

                        path.RemoveAt(path.Count - 1);
                        return true;
                    }

                    if (depState == NodeState.Unvisited)
                    {
                        if (DfsCycleDetect(normalizedDep, graph, state, path, result))
                            return true;
                    }
                }
            }
        }

        state[node] = NodeState.Done;
        path.RemoveAt(path.Count - 1);
        return false;
    }

    private enum NodeState
    {
        Unvisited,
        InProgress,
        Done
    }
}

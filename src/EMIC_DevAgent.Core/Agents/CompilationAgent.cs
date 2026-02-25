using EMIC.Shared.Services.Storage;
using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Configuration;
using EMIC_DevAgent.Core.Models.Generation;
using EMIC_DevAgent.Core.Services.Compilation;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Agents;

/// <summary>
/// Compila con XC16, parsea errores, retropropaga correcciones.
/// Reintenta hasta MaxCompilationRetries veces.
/// Uses SourceMapper for accurate error-to-source backtracking.
/// </summary>
public class CompilationAgent : AgentBase
{
    private readonly ICompilationService _compilationService;
    private readonly CompilationErrorParser _errorParser;
    private readonly SourceMapper _sourceMapper;
    private readonly EmicAgentConfig _config;
    private readonly MediaAccess _mediaAccess;

    public CompilationAgent(
        ICompilationService compilationService,
        CompilationErrorParser errorParser,
        SourceMapper sourceMapper,
        EmicAgentConfig config,
        MediaAccess mediaAccess,
        ILogger<CompilationAgent> logger)
        : base(logger)
    {
        _compilationService = compilationService;
        _errorParser = errorParser;
        _sourceMapper = sourceMapper;
        _config = config;
        _mediaAccess = mediaAccess;
    }

    public override string Name => "Compilation";
    public override string Description => "Compila con XC16, parsea errores, retropropaga correcciones";

    public override bool CanHandle(AgentContext context)
        => context.GeneratedFiles.Count > 0;

    protected override async Task<AgentResult> ExecuteCoreAsync(AgentContext context, CancellationToken ct)
    {
        var maxRetries = _config.MaxCompilationRetries;
        var projectPath = context.Properties.TryGetValue("ProjectPath", out var path)
            ? path.ToString() ?? string.Empty
            : string.Empty;

        var systemPath = context.Properties.TryGetValue("SystemPath", out var sp)
            ? sp.ToString() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrEmpty(projectPath))
        {
            Logger.LogError("ProjectPath not set in context — MaterializerAgent may not have run");
            return AgentResult.Failure(Name,
                "ProjectPath not set. Ensure MaterializerAgent runs before CompilationAgent to write files and set paths.");
        }

        Logger.LogInformation("Starting compilation (max {MaxRetries} attempts) for path: {Path}",
            maxRetries, projectPath);

        // Load .map TSV files for error-to-source resolution
        var mapFiles = _sourceMapper.LoadMapFiles(_mediaAccess, systemPath);

        CompilationResult? lastResult = null;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            Logger.LogInformation("Compilation attempt {Attempt}/{MaxRetries}", attempt, maxRetries);

            try
            {
                lastResult = await _compilationService.CompileAsync(projectPath, ct);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Compilation service threw exception on attempt {Attempt}", attempt);
                lastResult = new CompilationResult
                {
                    Success = false,
                    AttemptNumber = attempt
                };
                lastResult.Errors.Add($"Compilation service error: {ex.Message}");
            }

            lastResult.AttemptNumber = attempt;
            context.LastCompilation = lastResult;

            if (lastResult.Success)
            {
                Logger.LogInformation("Compilation succeeded on attempt {Attempt}", attempt);

                if (lastResult.Warnings.Count > 0)
                    Logger.LogWarning("Compilation warnings: {Warnings}", string.Join("; ", lastResult.Warnings));

                return AgentResult.Success(Name,
                    $"Compilation succeeded on attempt {attempt}" +
                    (lastResult.Warnings.Count > 0 ? $" with {lastResult.Warnings.Count} warnings" : ""));
            }

            Logger.LogWarning("Compilation failed on attempt {Attempt}: {ErrorCount} errors",
                attempt, lastResult.Errors.Count);

            // If this is the last attempt, don't try to fix
            if (attempt >= maxRetries)
                break;

            // Try to backtrack and fix errors
            var fixApplied = TryBacktrackAndFix(context, lastResult, mapFiles);

            if (!fixApplied)
            {
                Logger.LogWarning("Could not apply automatic fixes, stopping retry loop");
                break;
            }
        }

        // Compilation failed after all retries
        var errorSummary = lastResult != null
            ? string.Join("; ", lastResult.Errors.Take(5))
            : "Unknown compilation error";

        return AgentResult.Failure(Name,
            $"Compilation failed after {lastResult?.AttemptNumber ?? 0} attempts. Errors: {errorSummary}");
    }

    /// <summary>
    /// Attempts to backtrack compilation errors to generated source files and apply simple fixes.
    /// Uses SourceMapper with .map TSV files for accurate error-to-source mapping, with filename fallback.
    /// </summary>
    private bool TryBacktrackAndFix(AgentContext context, CompilationResult result, Dictionary<string, string> mapFiles)
    {
        bool anyFixed = false;

        // Parse structured errors from raw error strings
        var structuredErrors = new List<CompilationError>();
        foreach (var errorStr in result.Errors)
        {
            var parsed = _errorParser.Parse(errorStr);
            if (parsed.Count > 0)
                structuredErrors.AddRange(parsed);
            else
            {
                // Create a basic error from the raw string
                structuredErrors.Add(new CompilationError
                {
                    Message = errorStr,
                    Severity = "error"
                });
            }
        }

        foreach (var error in structuredErrors)
        {
            // Use SourceMapper with .map TSV files for accurate backtracking
            var mapped = _sourceMapper.MapError(error, context.GeneratedFiles, mapFiles);
            if (mapped == null)
                continue;

            var fixResult = TryApplySimpleFix(mapped.MappedFile, mapped.MappedLine, error);
            if (fixResult)
            {
                anyFixed = true;
                Logger.LogInformation("Applied fix for error in {File}:{Line}: {Error}",
                    mapped.MappedFile.RelativePath, mapped.MappedLine, Truncate(error.Message, 100));
            }
        }

        return anyFixed;
    }

    private bool TryApplySimpleFix(GeneratedFile file, int errorLine, CompilationError error)
    {
        var lowerMsg = error.Message.ToLowerInvariant();

        // Fix: undeclared identifier — might need a #include
        if (lowerMsg.Contains("undeclared") || lowerMsg.Contains("undefined"))
        {
            Logger.LogDebug("Detected undeclared/undefined error in {File}:{Line}", file.RelativePath, errorLine);
            return TryAddMissingInclude(file, error.Message);
        }

        // Fix: implicit declaration of function
        if (lowerMsg.Contains("implicit declaration"))
        {
            Logger.LogDebug("Detected implicit declaration in {File}:{Line}", file.RelativePath, errorLine);
            return TryAddMissingInclude(file, error.Message);
        }

        return false;
    }

    private static bool TryAddMissingInclude(GeneratedFile file, string errorMessage)
    {
        // Extract the missing identifier
        var funcMatch = System.Text.RegularExpressions.Regex.Match(errorMessage, @"'(\w+)'");
        if (!funcMatch.Success) return false;

        var identifier = funcMatch.Groups[1].Value;

        // Map known prefixes to headers
        string? header = null;
        if (identifier.StartsWith("HAL_GPIO")) header = "hal_gpio.h";
        else if (identifier.StartsWith("HAL_SPI")) header = "hal_spi.h";
        else if (identifier.StartsWith("HAL_I2C")) header = "hal_i2c.h";
        else if (identifier.StartsWith("HAL_UART")) header = "hal_uart.h";
        else if (identifier.StartsWith("HAL_ADC")) header = "hal_adc.h";
        else if (identifier.StartsWith("HAL_PWM")) header = "hal_pwm.h";
        else if (identifier.StartsWith("HAL_Timer")) header = "hal_timer.h";
        else if (identifier.StartsWith("getSystemMilis")) header = "system_time.h";

        if (header == null) return false;

        var include = $"#include \"{header}\"";
        if (file.Content.Contains(include)) return false;

        // Add the include after the last #include line
        var lines = file.Content.Split('\n').ToList();
        var lastInclude = lines.FindLastIndex(l => l.TrimStart().StartsWith("#include"));
        if (lastInclude >= 0)
        {
            lines.Insert(lastInclude + 1, include);
            file.Content = string.Join('\n', lines);
            return true;
        }

        return false;
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}

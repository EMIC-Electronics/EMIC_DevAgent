using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Configuration;
using EMIC_DevAgent.Core.Models.Generation;
using EMIC_DevAgent.Core.Services.Compilation;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Agents;

/// <summary>
/// Compila con XC16, parsea errores, retropropaga correcciones.
/// Reintenta hasta MaxCompilationRetries veces.
/// </summary>
public class CompilationAgent : AgentBase
{
    private readonly ICompilationService _compilationService;
    private readonly EmicAgentConfig _config;

    public CompilationAgent(ICompilationService compilationService, EmicAgentConfig config, ILogger<CompilationAgent> logger)
        : base(logger)
    {
        _compilationService = compilationService;
        _config = config;
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

        Logger.LogInformation("Starting compilation (max {MaxRetries} attempts) for path: {Path}",
            maxRetries, projectPath);

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
            var fixApplied = TryBacktrackAndFix(context, lastResult);

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
    /// Uses @source markers if present, otherwise matches by filename.
    /// </summary>
    private bool TryBacktrackAndFix(AgentContext context, CompilationResult result)
    {
        bool anyFixed = false;

        foreach (var error in result.Errors)
        {
            // Try to find the generated file that caused this error
            var sourceFile = BacktrackToGeneratedFile(context, error);
            if (sourceFile == null)
                continue;

            // Apply simple automatic fixes
            var fixResult = TryApplySimpleFix(sourceFile, error);
            if (fixResult)
            {
                anyFixed = true;
                Logger.LogInformation("Applied fix for error in {File}: {Error}",
                    sourceFile.RelativePath, Truncate(error, 100));
            }
        }

        return anyFixed;
    }

    private static GeneratedFile? BacktrackToGeneratedFile(AgentContext context, string errorMessage)
    {
        // Try to extract filename from error message (format: "file.c:42: error: ...")
        foreach (var file in context.GeneratedFiles.Where(f => f.Type == FileType.Source || f.Type == FileType.Header))
        {
            var fileName = Path.GetFileName(file.RelativePath);
            if (errorMessage.Contains(fileName, StringComparison.OrdinalIgnoreCase))
                return file;
        }

        return null;
    }

    private bool TryApplySimpleFix(GeneratedFile file, string error)
    {
        var lowerError = error.ToLowerInvariant();

        // Fix: missing semicolon
        if (lowerError.Contains("expected ';'") || lowerError.Contains("missing ';'"))
        {
            Logger.LogDebug("Detected missing semicolon error in {File}", file.RelativePath);
            // We can't auto-fix without knowing the exact line; flag for next attempt
            return false;
        }

        // Fix: undeclared identifier — might need a #include
        if (lowerError.Contains("undeclared") || lowerError.Contains("undefined"))
        {
            Logger.LogDebug("Detected undeclared/undefined error in {File}", file.RelativePath);
            // Add missing include if we can infer it
            return TryAddMissingInclude(file, error);
        }

        // Fix: implicit declaration of function
        if (lowerError.Contains("implicit declaration"))
        {
            Logger.LogDebug("Detected implicit declaration in {File}", file.RelativePath);
            return TryAddMissingInclude(file, error);
        }

        return false;
    }

    private static bool TryAddMissingInclude(GeneratedFile file, string error)
    {
        // Extract the missing identifier
        // Pattern: "undeclared identifier 'HAL_GPIO_PinCfg'" or similar
        var funcMatch = System.Text.RegularExpressions.Regex.Match(error, @"'(\w+)'");
        if (!funcMatch.Success) return false;

        var identifier = funcMatch.Groups[1].Value;

        // Map known prefixes to headers
        string? header = null;
        if (identifier.StartsWith("HAL_GPIO")) header = "hal_gpio.h";
        else if (identifier.StartsWith("HAL_SPI")) header = "hal_spi.h";
        else if (identifier.StartsWith("HAL_I2C")) header = "hal_i2c.h";
        else if (identifier.StartsWith("HAL_UART")) header = "hal_uart.h";
        else if (identifier.StartsWith("HAL_ADC")) header = "hal_adc.h";

        if (header == null) return false;

        var include = $"#include \"{header}\"";
        if (file.Content.Contains(include)) return false;

        // Add the include after the first #include line
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

using EMIC.Shared.Services.Build.Orchestration;
using EMIC.Shared.Services.Storage;
using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Configuration;
using Microsoft.Extensions.Logging;
using SharedCompilationResult = EMIC.Shared.Services.Emic.CompilationResult;

namespace EMIC_DevAgent.Core.Services.Compilation;

public class EmicCompilationService : ICompilationService
{
    private readonly MediaAccess _mediaAccess;
    private readonly IAgentSession _session;
    private readonly CompilationErrorParser _errorParser;
    private readonly ILogger<EmicCompilationService> _logger;

    public EmicCompilationService(
        MediaAccess mediaAccess,
        IAgentSession session,
        CompilationErrorParser errorParser,
        ILogger<EmicCompilationService> logger)
    {
        _mediaAccess = mediaAccess;
        _session = session;
        _errorParser = errorParser;
        _logger = logger;
    }

    public async Task<CompilationResult> CompileAsync(string projectPath, CancellationToken ct = default)
    {
        _logger.LogInformation("Compilando proyecto: {ProjectPath}", projectPath);

        // Resolve paths from the project module path
        // projectPath is expected to be like "USER:/MyProject/Module1" or a virtual path
        var targetPath = projectPath.TrimEnd('/') + "/Target";
        var systemPath = projectPath.TrimEnd('/') + "/System";
        var devPath = _session.SdkPath;

        // Extract module name from path (last segment)
        var moduleName = projectPath.Split('/', '\\')
            .LastOrDefault(s => !string.IsNullOrEmpty(s)) ?? "Module";

        // Read c_modules from Target directory if available
        var cModules = new List<string>();
        var cModulesPath = targetPath + "/c_modules.txt";
        if (_mediaAccess.FileExists(cModulesPath))
        {
            var content = _mediaAccess.FileReadAllText(cModulesPath);
            cModules = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        SharedCompilationResult sharedResult;
        try
        {
            sharedResult = await BuildService.BuildAsync(
                _mediaAccess,
                moduleName,
                targetPath,
                systemPath,
                devPath,
                cModules,
                _session.UserEmail,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante compilacion de {ModuleName}", moduleName);
            return new CompilationResult
            {
                Success = false,
                Errors = { $"Build exception: {ex.Message}" }
            };
        }

        var result = new CompilationResult
        {
            Success = sharedResult.Success
        };
        result.Errors.AddRange(sharedResult.Errors ?? []);
        result.Warnings.AddRange(sharedResult.Warnings ?? []);

        // Parse structured errors from compiler output if available
        if (!string.IsNullOrEmpty(sharedResult.CompilerOutput))
        {
            var parsedErrors = _errorParser.Parse(sharedResult.CompilerOutput);
            var errorMessages = parsedErrors
                .Where(e => e.Severity is "error" or "fatal error")
                .Select(e => $"{e.FilePath}:{e.Line}: {e.Message}");
            foreach (var msg in errorMessages)
            {
                if (!result.Errors.Contains(msg))
                    result.Errors.Add(msg);
            }
        }

        _logger.LogInformation("Compilacion {Status}: {ErrorCount} errores, {WarningCount} warnings",
            result.Success ? "exitosa" : "fallida", result.Errors.Count, result.Warnings.Count);

        return result;
    }
}

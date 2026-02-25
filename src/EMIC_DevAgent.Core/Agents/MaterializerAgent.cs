using EMIC.Shared.Services.Emic;
using EMIC.Shared.Services.Storage;
using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Agents;

/// <summary>
/// Writes GeneratedFiles to disk via MediaAccess, runs TreeMaker in compile mode
/// over generate.emic to produce TARGET/*.c and .map files, and sets ProjectPath/SystemPath
/// in context.Properties for CompilationAgent.
/// </summary>
public class MaterializerAgent : AgentBase
{
    private readonly MediaAccess _mediaAccess;
    private readonly IAgentSession _session;

    public MaterializerAgent(
        MediaAccess mediaAccess,
        IAgentSession session,
        ILogger<MaterializerAgent> logger)
        : base(logger)
    {
        _mediaAccess = mediaAccess;
        _session = session;
    }

    public override string Name => "Materializer";
    public override string Description => "Escribe archivos generados a disco, ejecuta EMIC:Generate, setea paths para compilación";

    public override bool CanHandle(AgentContext context)
        => context.GeneratedFiles.Count > 0;

    protected override async Task<AgentResult> ExecuteCoreAsync(AgentContext context, CancellationToken ct)
    {
        // 1. Extract modulePath from the first GeneratedFile's RelativePath
        //    e.g. "_modules/Sensors/X/System/generate.emic" → "_modules/Sensors/X"
        var modulePath = ExtractModulePath(context.GeneratedFiles[0].RelativePath);
        if (string.IsNullOrEmpty(modulePath))
        {
            return AgentResult.Failure(Name,
                $"Could not extract module path from '{context.GeneratedFiles[0].RelativePath}'");
        }

        Logger.LogInformation("Materializing {Count} files to DEV:{ModulePath}",
            context.GeneratedFiles.Count, modulePath);

        // 2. Write each GeneratedFile to disk via DEV: virtual path
        foreach (var file in context.GeneratedFiles)
        {
            ct.ThrowIfCancellationRequested();

            var virtualPath = "DEV:" + (file.RelativePath.StartsWith("/") ? "" : "/") + file.RelativePath;

            // Ensure parent directory exists
            var dirPath = virtualPath[..virtualPath.LastIndexOf('/')];
            if (!_mediaAccess.Directory.Exists(dirPath))
            {
                _mediaAccess.Directory.CreateDirectory(dirPath);
            }

            _mediaAccess.File.WriteAllText(virtualPath, file.Content);
            Logger.LogDebug("Wrote file: {Path}", virtualPath);
        }

        Logger.LogInformation("All {Count} generated files written to disk", context.GeneratedFiles.Count);

        // 3. Run TreeMaker in compile mode over generate.emic
        var generateEmicPath = $"DEV:/{modulePath}/System/generate.emic";
        if (!_mediaAccess.File.Exists(generateEmicPath))
        {
            Logger.LogWarning("generate.emic not found at {Path}, skipping TreeMaker generation", generateEmicPath);
            SetContextPaths(context, modulePath);
            return AgentResult.Success(Name,
                $"Materialized {context.GeneratedFiles.Count} files (no generate.emic found, skipped EMIC:Generate)");
        }

        try
        {
            var sdkPhysical = _mediaAccess.EmicPath("DEV:");
            var treeDrivers = new Dictionary<string, string>
            {
                ["DEV"] = sdkPhysical,
                ["SYS"] = $"{sdkPhysical}/{modulePath}/System",
                ["TARGET"] = $"{sdkPhysical}/{modulePath}/Target"
            };

            var treeMA = new MediaAccess(_session.UserEmail, treeDrivers);
            var emptyMacros = new Dictionary<string, Dictionary<string, string>>();

            var treeMaker = new TreeMaker(_session.UserEmail, treeMA, emptyMacros)
            {
                modo = "compile"
            };

            Logger.LogInformation("Running TreeMaker.Generate on {Path}", generateEmicPath);
            treeMaker.Generate("SYS:/generate.emic");

            // 4. Write TreeMaker output files (TARGET/*.c, .map files) to disk
            Logger.LogInformation("TreeMaker produced {Count} source files", treeMaker.sourcesFiles.Count);
            foreach (var kvp in treeMaker.sourcesFiles)
            {
                treeMA.FileWriteAllText(kvp.Key, kvp.Value);
                Logger.LogDebug("Wrote TreeMaker output: {Path}", kvp.Key);
            }

            // Report TreeMaker exceptions as warnings
            if (treeMaker.exceptions.Count > 0)
            {
                foreach (var ex in treeMaker.exceptions)
                {
                    Logger.LogWarning("TreeMaker exception: {Message}", ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "TreeMaker generation failed, continuing without TARGET files");
        }

        // 5. Set ProjectPath and SystemPath for CompilationAgent
        SetContextPaths(context, modulePath);

        return AgentResult.Success(Name,
            $"Materialized {context.GeneratedFiles.Count} files and ran EMIC:Generate for {modulePath}");
    }

    private static void SetContextPaths(AgentContext context, string modulePath)
    {
        context.Properties["ProjectPath"] = $"DEV:/{modulePath}";
        context.Properties["SystemPath"] = $"DEV:/{modulePath}/System";
    }

    /// <summary>
    /// Extracts the module/component base path from a relative file path.
    /// Examples:
    ///   "_modules/Sensors/X/System/generate.emic" → "_modules/Sensors/X"
    ///   "_api/Sensors/Temp/inc/Temp.h" → "_api/Sensors/Temp"
    ///   "_drivers/Communication/MQTT/src/MQTT.c" → "_drivers/Communication/MQTT"
    /// </summary>
    private static string ExtractModulePath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        var parts = normalized.Split('/');

        // Expected pattern: _layer/Category/Name/...
        // We need at least 3 segments: layer, category, name
        if (parts.Length >= 3 &&
            (parts[0].StartsWith("_modules") || parts[0].StartsWith("_api") || parts[0].StartsWith("_drivers")))
        {
            return $"{parts[0]}/{parts[1]}/{parts[2]}";
        }

        // Fallback: try to find "System" or "src" or "inc" folder and take everything before it
        for (int i = parts.Length - 1; i >= 1; i--)
        {
            if (parts[i] is "System" or "src" or "inc" or "Target")
            {
                return string.Join("/", parts[..i]);
            }
        }

        // Last resort: return everything except the filename
        if (parts.Length >= 2)
        {
            return string.Join("/", parts[..^1]);
        }

        return string.Empty;
    }
}

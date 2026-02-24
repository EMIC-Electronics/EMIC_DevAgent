using EMIC.Shared.Services.Emic;
using EMIC.Shared.Services.Storage;
using EMIC_DevAgent.Core.Configuration;
using EMIC_DevAgent.Core.Models.Sdk;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Services.Sdk;

/// <summary>
/// Parsea archivos .emic para extraer: setInput, copy, define, funciones declaradas.
/// Adapter sobre TreeMaker (modo Discovery).
/// </summary>
public class EmicFileParser
{
    private readonly MediaAccess _mediaAccess;
    private readonly IAgentSession _session;
    private readonly ILogger<EmicFileParser> _logger;

    public EmicFileParser(MediaAccess mediaAccess, IAgentSession session, ILogger<EmicFileParser> logger)
    {
        _mediaAccess = mediaAccess;
        _session = session;
        _logger = logger;
    }

    public Task<ApiDefinition> ParseApiEmicAsync(string filePath, CancellationToken ct = default)
    {
        _logger.LogDebug("Parsing API emic file: {FilePath}", filePath);

        var macros = new Dictionary<string, Dictionary<string, string>>();
        var treeMaker = new TreeMaker(_session.UserEmail, _mediaAccess, macros);
        treeMaker.modo = "Discovery";

        try
        {
            treeMaker.Generate(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TreeMaker.Generate failed for {FilePath}, returning partial result", filePath);
        }

        var def = MapToApiDefinition(filePath, treeMaker);
        return Task.FromResult(def);
    }

    public Task<DriverDefinition> ParseDriverEmicAsync(string filePath, CancellationToken ct = default)
    {
        _logger.LogDebug("Parsing driver emic file: {FilePath}", filePath);

        var macros = new Dictionary<string, Dictionary<string, string>>();
        var treeMaker = new TreeMaker(_session.UserEmail, _mediaAccess, macros);
        treeMaker.modo = "Discovery";

        try
        {
            treeMaker.Generate(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TreeMaker.Generate failed for {FilePath}, returning partial result", filePath);
        }

        var def = MapToDriverDefinition(filePath, treeMaker);
        return Task.FromResult(def);
    }

    public Task<List<string>> ExtractDependenciesAsync(string filePath, CancellationToken ct = default)
    {
        _logger.LogDebug("Extracting dependencies from: {FilePath}", filePath);

        if (!_mediaAccess.File.Exists(filePath))
        {
            _logger.LogWarning("File not found for dependency extraction: {FilePath}", filePath);
            return Task.FromResult(new List<string>());
        }

        var content = _mediaAccess.File.ReadAllText(filePath);
        var deps = content.Split('\n')
            .Select(l => l.TrimStart())
            .Where(l => l.StartsWith("EMIC:setInput"))
            .Select(l => ExtractSetInputPath(l))
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        return Task.FromResult(deps);
    }

    private static string ExtractSetInputPath(string line)
    {
        // EMIC:setInput(DEV:/_api/SomeApi/someapi.emic)
        var start = line.IndexOf('(');
        var end = line.IndexOf(')');
        if (start >= 0 && end > start)
            return line.Substring(start + 1, end - start - 1).Trim();
        return string.Empty;
    }

    private static ApiDefinition MapToApiDefinition(string filePath, TreeMaker treeMaker)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        var def = new ApiDefinition
        {
            EmicFilePath = filePath,
            Name = name
        };

        foreach (var driver in treeMaker.emicDrivers.Values)
        {
            foreach (var (category, elements) in driver.Content)
            {
                foreach (var elem in elements)
                {
                    if (elem.Attributes.TryGetValue("name", out var funcName) && !string.IsNullOrEmpty(funcName))
                        def.Functions.Add(funcName);
                }
            }
        }

        foreach (var sourceFile in treeMaker.sourcesFiles.Keys)
            def.Dependencies.Add(sourceFile);

        return def;
    }

    private static DriverDefinition MapToDriverDefinition(string filePath, TreeMaker treeMaker)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        var def = new DriverDefinition
        {
            EmicFilePath = filePath,
            Name = name
        };

        foreach (var driver in treeMaker.emicDrivers.Values)
        {
            foreach (var (category, elements) in driver.Content)
            {
                foreach (var elem in elements)
                {
                    if (elem.Attributes.TryGetValue("name", out var funcName) && !string.IsNullOrEmpty(funcName))
                        def.Functions.Add(funcName);
                }
            }
        }

        foreach (var sourceFile in treeMaker.sourcesFiles.Keys)
            def.HalDependencies.Add(sourceFile);

        return def;
    }
}

using EMIC_DevAgent.Core.Models.Sdk;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Services.Sdk;

/// <summary>
/// Parsea archivos .emic para extraer: setInput, copy, define, funciones declaradas.
/// </summary>
public class EmicFileParser
{
    private readonly ILogger<EmicFileParser> _logger;

    public EmicFileParser(ILogger<EmicFileParser> logger)
    {
        _logger = logger;
    }

    public Task<ApiDefinition> ParseApiEmicAsync(string filePath, CancellationToken ct = default)
    {
        throw new NotImplementedException("EmicFileParser.ParseApiEmicAsync pendiente de implementacion");
    }

    public Task<DriverDefinition> ParseDriverEmicAsync(string filePath, CancellationToken ct = default)
    {
        throw new NotImplementedException("EmicFileParser.ParseDriverEmicAsync pendiente de implementacion");
    }

    public Task<List<string>> ExtractDependenciesAsync(string filePath, CancellationToken ct = default)
    {
        throw new NotImplementedException("EmicFileParser.ExtractDependenciesAsync pendiente de implementacion");
    }
}

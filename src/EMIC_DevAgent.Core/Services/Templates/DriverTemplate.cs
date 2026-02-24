using EMIC_DevAgent.Core.Models.Generation;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Services.Templates;

/// <summary>
/// Template para generar drivers (.emic, .h, .c) siguiendo patrones como ADS1231.
/// </summary>
public class DriverTemplate
{
    private readonly ILogger<DriverTemplate> _logger;

    public DriverTemplate(ILogger<DriverTemplate> logger)
    {
        _logger = logger;
    }

    public Task<List<GeneratedFile>> GenerateAsync(string driverName, string chipType, Dictionary<string, string> variables, CancellationToken ct = default)
    {
        throw new NotImplementedException("DriverTemplate.GenerateAsync pendiente de implementacion");
    }
}

using EMIC_DevAgent.Core.Models.Generation;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Services.Templates;

/// <summary>
/// Template para generar APIs (.emic, .h, .c) siguiendo patrones como led.emic.
/// </summary>
public class ApiTemplate
{
    private readonly ILogger<ApiTemplate> _logger;

    public ApiTemplate(ILogger<ApiTemplate> logger)
    {
        _logger = logger;
    }

    public Task<List<GeneratedFile>> GenerateAsync(string apiName, string category, Dictionary<string, string> variables, CancellationToken ct = default)
    {
        throw new NotImplementedException("ApiTemplate.GenerateAsync pendiente de implementacion");
    }
}

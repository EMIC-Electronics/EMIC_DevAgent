using EMIC_DevAgent.Core.Models.Generation;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Services.Templates;

/// <summary>
/// Template para generar modulos (generate.emic, deploy.emic, m_description.json).
/// </summary>
public class ModuleTemplate
{
    private readonly ILogger<ModuleTemplate> _logger;

    public ModuleTemplate(ILogger<ModuleTemplate> logger)
    {
        _logger = logger;
    }

    public Task<List<GeneratedFile>> GenerateAsync(string moduleName, string category, Dictionary<string, string> variables, CancellationToken ct = default)
    {
        throw new NotImplementedException("ModuleTemplate.GenerateAsync pendiente de implementacion");
    }
}

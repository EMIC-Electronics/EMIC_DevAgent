using EMIC_DevAgent.Core.Models.Generation;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Services.Templates;

/// <summary>
/// Implementacion concreta de ITemplateEngine que delega a los templates especializados
/// (ApiTemplate, DriverTemplate, ModuleTemplate) segun el nombre del template solicitado.
/// </summary>
public class TemplateEngineService : ITemplateEngine
{
    private readonly ApiTemplate _apiTemplate;
    private readonly DriverTemplate _driverTemplate;
    private readonly ModuleTemplate _moduleTemplate;
    private readonly ILogger<TemplateEngineService> _logger;

    public TemplateEngineService(
        ApiTemplate apiTemplate,
        DriverTemplate driverTemplate,
        ModuleTemplate moduleTemplate,
        ILogger<TemplateEngineService> logger)
    {
        _apiTemplate = apiTemplate;
        _driverTemplate = driverTemplate;
        _moduleTemplate = moduleTemplate;
        _logger = logger;
    }

    public async Task<GeneratedFile> GenerateFromTemplateAsync(string templateName, Dictionary<string, string> variables, CancellationToken ct = default)
    {
        _logger.LogInformation("Generating from template '{TemplateName}'", templateName);

        var name = variables.GetValueOrDefault("name", "component");
        var category = variables.GetValueOrDefault("category", "General");

        List<GeneratedFile> files;

        switch (templateName.ToLowerInvariant())
        {
            case "api":
                files = await _apiTemplate.GenerateAsync(name, category, variables, ct);
                break;

            case "driver":
                var chipType = variables.GetValueOrDefault("chipType", name);
                files = await _driverTemplate.GenerateAsync(name, chipType, variables, ct);
                break;

            case "module":
                files = await _moduleTemplate.GenerateAsync(name, category, variables, ct);
                break;

            default:
                _logger.LogWarning("Unknown template '{TemplateName}', generating empty file", templateName);
                return new GeneratedFile
                {
                    RelativePath = $"{name}.txt",
                    Content = $"// Template '{templateName}' not found",
                    Type = FileType.Source,
                    GeneratedByAgent = "TemplateEngine"
                };
        }

        // Return the first file (primary); caller can use specialized templates for all files
        return files.FirstOrDefault() ?? new GeneratedFile
        {
            RelativePath = $"{name}.txt",
            Content = "// Empty template output",
            Type = FileType.Source,
            GeneratedByAgent = "TemplateEngine"
        };
    }

    public string ApplyVariables(string template, Dictionary<string, string> variables)
    {
        var result = template;
        foreach (var (key, value) in variables)
        {
            result = result.Replace($"{{{{{key}}}}}", value);   // {{key}} -> value
            result = result.Replace($"${{{key}}}", value);      // ${key} -> value
            result = result.Replace($"%{key}%", value);         // %key% -> value
        }
        return result;
    }
}

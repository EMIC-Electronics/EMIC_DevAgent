using System.Text.Json;
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
        _logger.LogInformation("Generating Module template for {ModuleName} in category {Category}", moduleName, category);

        var pcbName = variables.GetValueOrDefault("pcbName", moduleName);
        var description = variables.GetValueOrDefault("description", $"{moduleName} module");
        var toolTip = variables.GetValueOrDefault("toolTip", description);
        var features = variables.GetValueOrDefault("features", "");
        var applications = variables.GetValueOrDefault("applications", "");
        var keywords = variables.GetValueOrDefault("keywords", "");

        var basePath = $"_modules/{category}/{moduleName}";

        var files = new List<GeneratedFile>
        {
            new()
            {
                RelativePath = $"{basePath}/System/generate.emic",
                Content = GenerateGenerateEmic(moduleName, pcbName),
                Type = FileType.Emic,
                GeneratedByAgent = "ModuleTemplate"
            },
            new()
            {
                RelativePath = $"{basePath}/System/deploy.emic",
                Content = GenerateDeployEmic(moduleName),
                Type = FileType.Emic,
                GeneratedByAgent = "ModuleTemplate"
            },
            new()
            {
                RelativePath = $"{basePath}/m_description.json",
                Content = GenerateDescriptionJson(moduleName, description, toolTip, features, applications, keywords),
                Type = FileType.Json,
                GeneratedByAgent = "ModuleTemplate"
            }
        };

        _logger.LogInformation("Generated {Count} files for Module {ModuleName}", files.Count, moduleName);
        return Task.FromResult(files);
    }

    private static string GenerateGenerateEmic(string moduleName, string pcbName)
    {
        return $@"/// @file generate.emic
/// @brief Generation script for {moduleName} module

EMIC:setOutput(TARGET:generate.txt)

// PCB configuration
EMIC:setInput(DEV:_pcb/{pcbName}/{pcbName}.emic)

// User functions
EMIC:setInput(SYS:usedFunction.emic)
EMIC:setInput(SYS:usedEvent.emic)

// API layers
EMIC:setInput(SYS:usedApi.emic)

// Main entry point
EMIC:setInput(DEV:_main/main.emic)

// Copy user function files
EMIC:copy(userFncFile.c,TARGET:src/userFncFile.c)
EMIC:copy(inc/userFncFile.h,TARGET:inc/userFncFile.h)

// Copy MPLAB-X template
EMIC:setInput(DEV:_templates/mplab-X/mplab-X.emic)

EMIC:restoreOutput
";
    }

    private static string GenerateDeployEmic(string moduleName)
    {
        return $@"/// @file deploy.emic
/// @brief Deployment script for {moduleName} module

// Deploy EMIC-TABS resources
EMIC:setOutput(SYS:EMIC-TABS/Resources/resources.emic)
EMIC:copy(EMIC-TABS/Resources/resources.emic,SYS:EMIC-TABS/Resources/resources.emic)
EMIC:restoreOutput

EMIC:setOutput(SYS:EMIC-TABS/Data/data.emic)
EMIC:copy(EMIC-TABS/Data/data.emic,SYS:EMIC-TABS/Data/data.emic)
EMIC:restoreOutput

// Module ID header
EMIC:setOutput(SYS:inc/myId.h)
#define MODULE_ID "".{{module.Id}}.""
EMIC:restoreOutput
";
    }

    private static string GenerateDescriptionJson(string moduleName, string description, string toolTip,
        string features, string applications, string keywords)
    {
        var featureList = ParseCommaSeparated(features);
        var applicationList = ParseCommaSeparated(applications);
        var keywordList = ParseCommaSeparated(keywords);

        var obj = new
        {
            type = moduleName,
            toolTip,
            description,
            features = featureList,
            applications = applicationList,
            keyWord = keywordList
        };

        return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string[] ParseCommaSeparated(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Array.Empty<string>();
        return input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

using System.Text.Json;
using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Models.Generation;
using EMIC_DevAgent.Core.Services.Llm;
using EMIC_DevAgent.Core.Services.Templates;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Agents;

/// <summary>
/// Genera generate.emic, deploy.emic y m_description.json para modulos completos.
/// Sigue patrones existentes como HRD_LoRaWan/System/generate.emic.
/// </summary>
public class ModuleGeneratorAgent : AgentBase
{
    private readonly ILlmService _llmService;
    private readonly ITemplateEngine _templateEngine;

    public ModuleGeneratorAgent(ILlmService llmService, ITemplateEngine templateEngine, ILogger<ModuleGeneratorAgent> logger)
        : base(logger)
    {
        _llmService = llmService;
        _templateEngine = templateEngine;
    }

    public override string Name => "ModuleGenerator";
    public override string Description => "Genera modulos completos (generate.emic, deploy.emic, m_description.json)";

    public override bool CanHandle(AgentContext context)
        => context.Analysis?.Intent == IntentType.CreateModule;

    protected override async Task<AgentResult> ExecuteCoreAsync(AgentContext context, CancellationToken ct)
    {
        var analysis = context.Analysis!;
        var moduleName = analysis.ComponentName;
        var category = analysis.Category;

        Logger.LogInformation("Generating Module '{ModuleName}' in category '{Category}'", moduleName, category);

        // 1. Resolve module variables using LLM
        var variables = await ResolveVariablesAsync(analysis, ct);

        // 2. Determine required APIs and drivers from context
        var requiredApis = analysis.RequiredDependencies
            .Where(d => context.GeneratedFiles.Any(f => f.RelativePath.Contains($"_api/") && f.RelativePath.Contains(d)))
            .ToList();

        var requiredDrivers = analysis.RequiredDependencies
            .Where(d => context.GeneratedFiles.Any(f => f.RelativePath.Contains($"_drivers/") && f.RelativePath.Contains(d)))
            .ToList();

        // 3. Generate module files
        var basePath = $"_modules/{category}/{moduleName}";
        var pcbName = variables.GetValueOrDefault("pcbName", moduleName);

        var files = new List<GeneratedFile>
        {
            new()
            {
                RelativePath = $"{basePath}/System/generate.emic",
                Content = GenerateGenerateEmic(moduleName, pcbName, requiredApis, requiredDrivers),
                Type = FileType.Emic,
                GeneratedByAgent = Name
            },
            new()
            {
                RelativePath = $"{basePath}/System/deploy.emic",
                Content = GenerateDeployEmic(moduleName),
                Type = FileType.Emic,
                GeneratedByAgent = Name
            },
            new()
            {
                RelativePath = $"{basePath}/m_description.json",
                Content = GenerateDescriptionJson(moduleName, variables),
                Type = FileType.Json,
                GeneratedByAgent = Name
            }
        };

        // 4. Add files to context
        foreach (var file in files)
            context.GeneratedFiles.Add(file);

        // 5. Mark that this module may need program.xml
        if (context.Plan != null)
        {
            context.Plan.RequiresProgramXml = true;
            foreach (var file in files)
            {
                context.Plan.FilesToGenerate.Add(new PlannedFile
                {
                    RelativePath = file.RelativePath,
                    Type = file.Type,
                    Purpose = $"Module {moduleName}: {file.Type}"
                });
            }
        }

        Logger.LogInformation("Module '{ModuleName}' generated: {Count} files", moduleName, files.Count);
        return AgentResult.Success(Name, $"Generated module '{moduleName}' with {files.Count} files");
    }

    private async Task<Dictionary<string, string>> ResolveVariablesAsync(PromptAnalysis analysis, CancellationToken ct)
    {
        var prompt = new LlmPromptBuilder()
            .WithSystemInstruction(
                "You are an EMIC SDK expert. Given a module description, extract these variables as key=value lines:\n" +
                "pcbName: PCB board name (e.g., HRD_X2_RELAY, HRD_LoRaWan)\n" +
                "description: module description\n" +
                "toolTip: short tooltip text\n" +
                "features: comma-separated list of features\n" +
                "applications: comma-separated list of applications\n" +
                "keywords: comma-separated keywords for search\n" +
                "Respond ONLY with key=value lines.")
            .WithUserPrompt($"Module: {analysis.ComponentName}\nCategory: {analysis.Category}\nDescription: {analysis.Description}")
            .Build();

        var response = await _llmService.GenerateWithContextAsync(prompt.UserPrompt, prompt.SystemPrompt, ct);
        return ParseKeyValueResponse(response, analysis);
    }

    private static Dictionary<string, string> ParseKeyValueResponse(string response, PromptAnalysis analysis)
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["pcbName"] = analysis.ComponentName,
            ["description"] = analysis.Description,
            ["toolTip"] = analysis.Description,
            ["features"] = "",
            ["applications"] = "",
            ["keywords"] = analysis.ComponentName
        };

        foreach (var line in response.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIdx = line.IndexOf('=');
            if (eqIdx <= 0) continue;
            var key = line[..eqIdx].Trim().ToLowerInvariant();
            var value = line[(eqIdx + 1)..].Trim();
            if (!string.IsNullOrEmpty(value))
                vars[key] = value;
        }

        return vars;
    }

    private static string GenerateGenerateEmic(string moduleName, string pcbName,
        List<string> apis, List<string> drivers)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"/// @file generate.emic");
        sb.AppendLine($"/// @brief Generation script for {moduleName} module");
        sb.AppendLine();
        sb.AppendLine("EMIC:setOutput(TARGET:generate.txt)");
        sb.AppendLine();
        sb.AppendLine("// PCB configuration");
        sb.AppendLine($"EMIC:setInput(DEV:_pcb/{pcbName}/{pcbName}.emic)");
        sb.AppendLine();
        sb.AppendLine("// User functions and events");
        sb.AppendLine("EMIC:setInput(SYS:usedFunction.emic)");
        sb.AppendLine("EMIC:setInput(SYS:usedEvent.emic)");
        sb.AppendLine();

        // Include required APIs with their parameters
        if (apis.Count > 0)
        {
            sb.AppendLine("// API layers");
            foreach (var api in apis)
                sb.AppendLine($"EMIC:setInput(SYS:{api}.emic)");
            sb.AppendLine();
        }

        // Include required drivers
        if (drivers.Count > 0)
        {
            sb.AppendLine("// Drivers");
            foreach (var driver in drivers)
                sb.AppendLine($"EMIC:setInput(SYS:{driver}.emic)");
            sb.AppendLine();
        }

        sb.AppendLine("// Main entry point");
        sb.AppendLine("EMIC:setInput(DEV:_main/main.emic)");
        sb.AppendLine();
        sb.AppendLine("// Copy user function files");
        sb.AppendLine("EMIC:copy(userFncFile.c,TARGET:src/userFncFile.c)");
        sb.AppendLine("EMIC:copy(inc/userFncFile.h,TARGET:inc/userFncFile.h)");
        sb.AppendLine();
        sb.AppendLine("// Copy MPLAB-X template");
        sb.AppendLine("EMIC:setInput(DEV:_templates/mplab-X/mplab-X.emic)");
        sb.AppendLine();
        sb.AppendLine("EMIC:restoreOutput");

        return sb.ToString();
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

    private static string GenerateDescriptionJson(string moduleName, Dictionary<string, string> variables)
    {
        var description = variables.GetValueOrDefault("description", $"{moduleName} module");
        var toolTip = variables.GetValueOrDefault("toolTip", description);
        var features = ParseCsv(variables.GetValueOrDefault("features", ""));
        var applications = ParseCsv(variables.GetValueOrDefault("applications", ""));
        var keywords = ParseCsv(variables.GetValueOrDefault("keywords", moduleName));

        var obj = new
        {
            type = moduleName,
            toolTip,
            description,
            features,
            applications,
            keyWord = keywords
        };

        return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string[] ParseCsv(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Array.Empty<string>();
        return input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Models.Generation;
using EMIC_DevAgent.Core.Services.Llm;
using EMIC_DevAgent.Core.Services.Templates;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Agents;

/// <summary>
/// Genera drivers para chips externos (.emic, .h, .c) usando HAL.
/// Sigue patrones como ADS1231 driver.
/// </summary>
public class DriverGeneratorAgent : AgentBase
{
    private readonly ILlmService _llmService;
    private readonly ITemplateEngine _templateEngine;

    public DriverGeneratorAgent(ILlmService llmService, ITemplateEngine templateEngine, ILogger<DriverGeneratorAgent> logger)
        : base(logger)
    {
        _llmService = llmService;
        _templateEngine = templateEngine;
    }

    public override string Name => "DriverGenerator";
    public override string Description => "Genera drivers para chips externos (.emic, .h, .c) usando HAL";

    public override bool CanHandle(AgentContext context)
        => context.Analysis?.Intent == IntentType.CreateDriver;

    protected override async Task<AgentResult> ExecuteCoreAsync(AgentContext context, CancellationToken ct)
    {
        var analysis = context.Analysis!;
        var driverName = analysis.ComponentName;
        var category = analysis.Category;

        Logger.LogInformation("Generating Driver '{DriverName}' in category '{Category}'", driverName, category);

        // 1. Resolve variables using LLM
        var variables = await ResolveVariablesAsync(analysis, ct);
        var halDep = variables.GetValueOrDefault("halDependency", "GPIO");
        var chipType = variables.GetValueOrDefault("chipType", driverName);

        // 2. Use LLM to generate driver implementation
        var files = await GenerateDriverFilesAsync(driverName, category, chipType, halDep, analysis.Description, ct);

        // 3. Add files to context
        foreach (var file in files)
            context.GeneratedFiles.Add(file);

        // 4. Update plan
        if (context.Plan != null)
        {
            foreach (var file in files)
            {
                context.Plan.FilesToGenerate.Add(new PlannedFile
                {
                    RelativePath = file.RelativePath,
                    Type = file.Type,
                    Purpose = $"Driver {driverName}: {file.Type}"
                });
            }
        }

        Logger.LogInformation("Driver '{DriverName}' generated: {Count} files", driverName, files.Count);
        return AgentResult.Success(Name, $"Generated driver '{driverName}' with {files.Count} files");
    }

    private async Task<Dictionary<string, string>> ResolveVariablesAsync(PromptAnalysis analysis, CancellationToken ct)
    {
        var prompt = new LlmPromptBuilder()
            .WithSystemInstruction(
                "You are an EMIC SDK expert. Given a driver description, extract these variables as key=value lines:\n" +
                "chipType: the IC/chip model (e.g., ADS1231, MCP2200, LM35)\n" +
                "halDependency: required HAL interface (GPIO, SPI, I2C, UART, ADC)\n" +
                "category: driver category (Sensors, Communication, Display, Actuators)\n" +
                "description: one-line description\n" +
                "Respond ONLY with key=value lines.")
            .WithUserPrompt($"Driver: {analysis.ComponentName}\nCategory: {analysis.Category}\nDescription: {analysis.Description}")
            .Build();

        var response = await _llmService.GenerateWithContextAsync(prompt.UserPrompt, prompt.SystemPrompt, ct);
        return ParseKeyValueResponse(response, analysis);
    }

    private static Dictionary<string, string> ParseKeyValueResponse(string response, PromptAnalysis analysis)
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["chipType"] = analysis.ComponentName,
            ["halDependency"] = "GPIO",
            ["category"] = analysis.Category,
            ["description"] = analysis.Description
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

    private async Task<List<GeneratedFile>> GenerateDriverFilesAsync(string driverName, string category,
        string chipType, string halDep, string description, CancellationToken ct)
    {
        var basePath = $"_drivers/{category}/{driverName}";

        // Use LLM for the source implementation
        var prompt = new LlmPromptBuilder()
            .WithSystemInstruction(
                "You are an EMIC embedded C expert. Generate a driver for an external chip.\n" +
                "Rules:\n" +
                "- Use HAL_* functions only (HAL_GPIO_*, HAL_SPI_*, HAL_I2C_*, HAL_UART_*)\n" +
                "- No __delay_ms() or blocking loops\n" +
                "- Include init, read, write functions at minimum\n" +
                "- Use descriptive function names that match the driver category " +
                "(e.g., getTemperature for temperature sensors)\n" +
                "- Make drivers interchangeable within their category\n" +
                "Respond with the .c file content ONLY, no markdown fences.")
            .WithUserPrompt($"Driver: {driverName}\nChip: {chipType}\nHAL: {halDep}\nDescription: {description}")
            .Build();

        string sourceContent;
        try
        {
            sourceContent = await _llmService.GenerateWithContextAsync(prompt.UserPrompt, prompt.SystemPrompt, ct);
            sourceContent = StripMarkdownFences(sourceContent);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "LLM generation failed, using template defaults");
            sourceContent = GenerateDefaultSource(driverName, chipType);
        }

        return new List<GeneratedFile>
        {
            new()
            {
                RelativePath = $"{basePath}/{driverName}.emic",
                Content = GenerateEmicFile(driverName, halDep, description),
                Type = FileType.Emic,
                GeneratedByAgent = Name
            },
            new()
            {
                RelativePath = $"{basePath}/inc/{driverName}.h",
                Content = GenerateHeaderFile(driverName, chipType, description),
                Type = FileType.Header,
                GeneratedByAgent = Name
            },
            new()
            {
                RelativePath = $"{basePath}/src/{driverName}.c",
                Content = sourceContent,
                Type = FileType.Source,
                GeneratedByAgent = Name
            }
        };
    }

    private static string GenerateEmicFile(string driverName, string halDep, string description)
    {
        return $@"//EMIC:tag({driverName})
/// @file {driverName}.emic
/// @brief {description}

EMIC:ifndef({driverName}_included)
EMIC:define({driverName}_included,1)

EMIC:setInput(DEV:_hal/{halDep}/{halDep.ToLowerInvariant()}.emic)

EMIC:copy(inc/{driverName}.h,TARGET:inc/{driverName}.h)
EMIC:copy(src/{driverName}.c,TARGET:src/{driverName}.c)

EMIC:define(c_modules.{driverName},TARGET:src/{driverName}.c)

EMIC:endif
";
    }

    private static string GenerateHeaderFile(string driverName, string chipType, string description)
    {
        var guard = $"_{driverName.ToUpperInvariant()}_H_";
        return $@"#ifndef {guard}
#define {guard}

/// @file {driverName}.h
/// @brief {description} ({chipType})

#include <stdint.h>

void {driverName}_init(void);
uint8_t {driverName}_read(void);
void {driverName}_write(uint8_t data);

#endif // {guard}
";
    }

    private static string GenerateDefaultSource(string driverName, string chipType)
    {
        return $@"#include ""{driverName}.h""
#include ""hal_gpio.h""

void {driverName}_init(void) {{
    // TODO: Initialize {chipType} hardware
}}

uint8_t {driverName}_read(void) {{
    // TODO: Implement {chipType} read
    return 0;
}}

void {driverName}_write(uint8_t data) {{
    // TODO: Implement {chipType} write
}}
";
    }

    private static string StripMarkdownFences(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
                trimmed = trimmed[(firstNewline + 1)..];
        }
        if (trimmed.EndsWith("```"))
            trimmed = trimmed[..^3].TrimEnd();
        return trimmed;
    }
}

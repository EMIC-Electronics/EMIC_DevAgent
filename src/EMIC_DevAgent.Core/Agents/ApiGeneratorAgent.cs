using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Models.Generation;
using EMIC_DevAgent.Core.Services.Llm;
using EMIC_DevAgent.Core.Services.Templates;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Agents;

/// <summary>
/// Genera archivos .emic, .h, .c para nuevas APIs siguiendo patrones
/// existentes como led.emic y relay.emic.
/// </summary>
public class ApiGeneratorAgent : AgentBase
{
    private readonly ILlmService _llmService;
    private readonly ITemplateEngine _templateEngine;

    public ApiGeneratorAgent(ILlmService llmService, ITemplateEngine templateEngine, ILogger<ApiGeneratorAgent> logger)
        : base(logger)
    {
        _llmService = llmService;
        _templateEngine = templateEngine;
    }

    public override string Name => "ApiGenerator";
    public override string Description => "Genera APIs (.emic, .h, .c) siguiendo patrones del SDK";

    public override bool CanHandle(AgentContext context)
        => context.Analysis?.Intent == IntentType.CreateApi;

    protected override async Task<AgentResult> ExecuteCoreAsync(AgentContext context, CancellationToken ct)
    {
        var analysis = context.Analysis!;
        var apiName = analysis.ComponentName;
        var category = analysis.Category;

        Logger.LogInformation("Generating API '{ApiName}' in category '{Category}'", apiName, category);

        // 1. Use LLM to determine implementation details from the description
        var variables = await ResolveVariablesAsync(analysis, ct);

        // 2. Generate base files using template
        var templateVars = new Dictionary<string, string>
        {
            ["apiName"] = apiName,
            ["category"] = category
        };
        foreach (var kv in variables)
            templateVars[kv.Key] = kv.Value;

        var templateFile = await _templateEngine.GenerateFromTemplateAsync("api", templateVars, ct);

        // 3. Use LLM to enhance the source code with actual implementation
        var enhancedFiles = await EnhanceWithLlmAsync(apiName, category, variables, analysis.Description, ct);

        // 4. Add all generated files to context
        foreach (var file in enhancedFiles)
        {
            context.GeneratedFiles.Add(file);
        }

        // 5. Update plan if present
        if (context.Plan != null)
        {
            foreach (var file in enhancedFiles)
            {
                context.Plan.FilesToGenerate.Add(new PlannedFile
                {
                    RelativePath = file.RelativePath,
                    Type = file.Type,
                    Purpose = $"API {apiName}: {file.Type}"
                });
            }
        }

        Logger.LogInformation("API '{ApiName}' generated: {Count} files", apiName, enhancedFiles.Count);
        return AgentResult.Success(Name, $"Generated API '{apiName}' with {enhancedFiles.Count} files");
    }

    private async Task<Dictionary<string, string>> ResolveVariablesAsync(PromptAnalysis analysis, CancellationToken ct)
    {
        var prompt = new LlmPromptBuilder()
            .WithSystemInstruction(
                "You are an EMIC SDK expert. Given a description of an API to create, " +
                "extract the following variables as key=value pairs, one per line:\n" +
                "name: function prefix (e.g., 'led', 'relay', 'temperature')\n" +
                "pin: default GPIO pin (e.g., 'PIN_0')\n" +
                "driverName: lowercase driver name\n" +
                "description: one-line description\n" +
                "halDependency: required HAL (GPIO, SPI, I2C, UART, ADC)\n" +
                "Respond ONLY with key=value lines, no explanations.")
            .WithUserPrompt($"API name: {analysis.ComponentName}\nCategory: {analysis.Category}\nDescription: {analysis.Description}")
            .Build();

        var response = await _llmService.GenerateWithContextAsync(prompt.UserPrompt, prompt.SystemPrompt, ct);
        return ParseKeyValueResponse(response, analysis);
    }

    private static Dictionary<string, string> ParseKeyValueResponse(string response, PromptAnalysis analysis)
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = analysis.ComponentName.ToLowerInvariant(),
            ["pin"] = "PIN_0",
            ["driverName"] = analysis.ComponentName.ToLowerInvariant(),
            ["description"] = analysis.Description,
            ["halDependency"] = "GPIO"
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

    private async Task<List<GeneratedFile>> EnhanceWithLlmAsync(string apiName, string category,
        Dictionary<string, string> variables, string description, CancellationToken ct)
    {
        var name = variables.GetValueOrDefault("name", apiName);
        var halDep = variables.GetValueOrDefault("halDependency", "GPIO");
        var basePath = $"_api/{category}/{apiName}";

        var prompt = new LlmPromptBuilder()
            .WithSystemInstruction(
                "You are an EMIC embedded C expert. Generate non-blocking C code for an API.\n" +
                "Rules:\n" +
                "- Use HAL_* functions only (never direct register access)\n" +
                "- No __delay_ms() or blocking loops\n" +
                "- Use getSystemMilis() + state machines for timing\n" +
                "- Include init, on, off, toggle functions at minimum\n" +
                "- Wrap optional functions with EMIC:ifdef guards\n" +
                "- Use EMIC:define for inits, polls, c_modules\n" +
                "Respond with the .c file content ONLY, no markdown fences.")
            .WithUserPrompt($"Generate {apiName} API source code.\nDescription: {description}\nHAL: {halDep}\nFunction prefix: {name}")
            .Build();

        string sourceContent;
        try
        {
            sourceContent = await _llmService.GenerateWithContextAsync(prompt.UserPrompt, prompt.SystemPrompt, ct);
            // Strip markdown fences if LLM adds them anyway
            sourceContent = StripMarkdownFences(sourceContent);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "LLM enhancement failed, using template defaults");
            sourceContent = GenerateDefaultSource(apiName, name, variables.GetValueOrDefault("pin", "PIN_0"));
        }

        var files = new List<GeneratedFile>
        {
            new()
            {
                RelativePath = $"{basePath}/{apiName}.emic",
                Content = GenerateEmicFile(apiName, name, halDep, description),
                Type = FileType.Emic,
                GeneratedByAgent = Name
            },
            new()
            {
                RelativePath = $"{basePath}/inc/{apiName}.h",
                Content = GenerateHeaderFile(apiName, name, description),
                Type = FileType.Header,
                GeneratedByAgent = Name
            },
            new()
            {
                RelativePath = $"{basePath}/src/{apiName}.c",
                Content = sourceContent,
                Type = FileType.Source,
                GeneratedByAgent = Name
            }
        };

        return files;
    }

    private static string GenerateEmicFile(string apiName, string name, string halDep, string description)
    {
        return $@"//EMIC:tag({apiName})
/// @file {apiName}.emic
/// @brief {description}

/// @fn {name}_init
/// @brief Initializes the {name} API

EMIC:setInput(DEV:_hal/{halDep}/{halDep.ToLowerInvariant()}.emic)

EMIC:copy(inc/{apiName}.h,TARGET:inc/{apiName}.h)
EMIC:copy(src/{apiName}.c,TARGET:src/{apiName}.c)

EMIC:define(main_includes.{apiName},#include ""{apiName}.h"")
EMIC:define(c_modules.{apiName},TARGET:src/{apiName}.c)
EMIC:define(inits.{apiName},{name}_init)
";
    }

    private static string GenerateHeaderFile(string apiName, string name, string description)
    {
        var guard = $"_{apiName.ToUpperInvariant()}_H_";
        return $@"#ifndef {guard}
#define {guard}

/// @file {apiName}.h
/// @brief {description}

#include <xc.h>
#include <stdint.h>

void {name}_init(void);

EMIC:ifdef({apiName}.on)
void {name}_on(void);
EMIC:endif

EMIC:ifdef({apiName}.off)
void {name}_off(void);
EMIC:endif

EMIC:ifdef({apiName}.toggle)
void {name}_toggle(void);
EMIC:endif

EMIC:ifdef({apiName}.poll)
void {name}_poll(void);
EMIC:endif

#endif // {guard}
";
    }

    private static string GenerateDefaultSource(string apiName, string name, string pin)
    {
        return $@"#include ""{apiName}.h""
#include ""hal_gpio.h""

static uint8_t {name}_state = 0;

void {name}_init(void) {{
    HAL_GPIO_PinCfg({pin}, GPIO_OUTPUT);
    HAL_GPIO_WritePin({pin}, 0);
    {name}_state = 0;
}}

EMIC:ifdef({apiName}.on)
void {name}_on(void) {{
    HAL_GPIO_WritePin({pin}, 1);
    {name}_state = 1;
}}
EMIC:endif

EMIC:ifdef({apiName}.off)
void {name}_off(void) {{
    HAL_GPIO_WritePin({pin}, 0);
    {name}_state = 0;
}}
EMIC:endif

EMIC:ifdef({apiName}.toggle)
void {name}_toggle(void) {{
    {name}_state = !{name}_state;
    HAL_GPIO_WritePin({pin}, {name}_state);
}}
EMIC:endif
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

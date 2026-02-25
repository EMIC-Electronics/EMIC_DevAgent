using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Models.Generation;
using EMIC_DevAgent.Core.Services.Llm;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Agents;

/// <summary>
/// Genera program.xml y archivos asociados para la logica de integracion.
/// </summary>
public class ProgramXmlAgent : AgentBase
{
    private readonly ILlmService _llmService;

    public ProgramXmlAgent(ILlmService llmService, ILogger<ProgramXmlAgent> logger)
        : base(logger)
    {
        _llmService = llmService;
    }

    public override string Name => "ProgramXml";
    public override string Description => "Genera program.xml y archivos de integracion";

    public override bool CanHandle(AgentContext context)
        => context.Plan?.RequiresProgramXml == true;

    protected override async Task<AgentResult> ExecuteCoreAsync(AgentContext context, CancellationToken ct)
    {
        var analysis = context.Analysis;
        var moduleName = analysis?.ComponentName ?? "module";

        Logger.LogInformation("Generating program.xml for module '{ModuleName}'", moduleName);

        // 1. Collect all available functions and events from generated files
        var functions = ExtractFunctionsFromGeneratedFiles(context);
        var events = ExtractEventsFromGeneratedFiles(context);

        Logger.LogDebug("Found {FuncCount} functions and {EventCount} events for program.xml",
            functions.Count, events.Count);

        // 2. Use LLM to generate program.xml content
        var programXml = await GenerateProgramXmlAsync(moduleName, functions, events, analysis?.Description ?? "", ct);

        // 3. Generate userFncFile.c and userFncFile.h
        var userFncC = GenerateUserFncFile(moduleName, functions);
        var userFncH = GenerateUserFncHeader(moduleName, functions);

        // 4. Determine module base path
        var category = analysis?.Category ?? "General";
        var basePath = $"_modules/{category}/{moduleName}/System";

        var files = new List<GeneratedFile>
        {
            new()
            {
                RelativePath = $"{basePath}/program.xml",
                Content = programXml,
                Type = FileType.Xml,
                GeneratedByAgent = Name
            },
            new()
            {
                RelativePath = $"{basePath}/userFncFile.c",
                Content = userFncC,
                Type = FileType.Source,
                GeneratedByAgent = Name
            },
            new()
            {
                RelativePath = $"{basePath}/inc/userFncFile.h",
                Content = userFncH,
                Type = FileType.Header,
                GeneratedByAgent = Name
            }
        };

        foreach (var file in files)
            context.GeneratedFiles.Add(file);

        Logger.LogInformation("ProgramXml generated {Count} files for '{ModuleName}'", files.Count, moduleName);
        return AgentResult.Success(Name, $"Generated program.xml and user files for '{moduleName}'");
    }

    private static List<string> ExtractFunctionsFromGeneratedFiles(AgentContext context)
    {
        var functions = new List<string>();

        foreach (var file in context.GeneratedFiles.Where(f => f.Type == FileType.Header))
        {
            // Extract function declarations: void funcName(params);
            foreach (var line in file.Content.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("EMIC:") || trimmed.StartsWith("//") || trimmed.StartsWith("#"))
                    continue;

                if (trimmed.Contains('(') && trimmed.EndsWith(";") &&
                    (trimmed.StartsWith("void ") || trimmed.StartsWith("uint") ||
                     trimmed.StartsWith("int ") || trimmed.StartsWith("char ")))
                {
                    var funcName = ExtractFunctionName(trimmed);
                    if (!string.IsNullOrEmpty(funcName) && !funcName.EndsWith("_init"))
                        functions.Add(funcName);
                }
            }
        }

        return functions.Distinct().ToList();
    }

    private static List<string> ExtractEventsFromGeneratedFiles(AgentContext context)
    {
        var events = new List<string>();

        foreach (var file in context.GeneratedFiles.Where(f => f.Type == FileType.Emic))
        {
            foreach (var line in file.Content.Split('\n'))
            {
                var trimmed = line.Trim();
                // Look for EMIC:define(events.xxx, ...) patterns
                if (trimmed.Contains("EMIC:define(events.") || trimmed.Contains("EMIC:define(polls."))
                {
                    var start = trimmed.IndexOf("events.", StringComparison.Ordinal);
                    if (start < 0) start = trimmed.IndexOf("polls.", StringComparison.Ordinal);
                    if (start >= 0)
                    {
                        var comma = trimmed.IndexOf(',', start);
                        var paren = trimmed.IndexOf(')', start);
                        var end = comma > 0 ? comma : paren;
                        if (end > start)
                            events.Add(trimmed[start..end]);
                    }
                }
            }
        }

        return events.Distinct().ToList();
    }

    private static string ExtractFunctionName(string declaration)
    {
        // "void funcName(void);" -> "funcName"
        var parenIdx = declaration.IndexOf('(');
        if (parenIdx < 0) return string.Empty;

        var beforeParen = declaration[..parenIdx].Trim();
        var spaceIdx = beforeParen.LastIndexOf(' ');
        if (spaceIdx < 0) return string.Empty;

        return beforeParen[(spaceIdx + 1)..].Trim('*');
    }

    private async Task<string> GenerateProgramXmlAsync(string moduleName, List<string> functions,
        List<string> events, string description, CancellationToken ct)
    {
        if (functions.Count == 0)
            return GenerateMinimalProgramXml(moduleName, description);

        var prompt = new LlmPromptBuilder()
            .WithSystemInstruction(
                "You are an EMIC program.xml expert. Generate valid EMIC program.xml content.\n" +
                "Rules:\n" +
                "- Use emic-function elements to call available functions\n" +
                "- Use emic-event elements for event handlers\n" +
                "- Parameters use emic-function-parameter with correct types (char, char*, uint8_t, etc.)\n" +
                "- Literals: emic-literal-char for char type, emic-literal-string for char*, emic-literal-numerical for numeric\n" +
                "- Root element is <emic-program>\n" +
                "- Include a setup section and main logic\n" +
                "Respond with XML content ONLY, no markdown fences.")
            .WithUserPrompt(
                $"Module: {moduleName}\nDescription: {description}\n" +
                $"Available functions: {string.Join(", ", functions)}\n" +
                $"Available events: {string.Join(", ", events)}")
            .Build();

        try
        {
            var xml = await _llmService.GenerateWithContextAsync(prompt.UserPrompt, prompt.SystemPrompt, ct);
            return StripMarkdownFences(xml);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "LLM program.xml generation failed, using minimal template");
            return GenerateMinimalProgramXml(moduleName, description);
        }
    }

    private static string GenerateMinimalProgramXml(string moduleName, string description)
    {
        return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<emic-program name=""{moduleName}"" description=""{EscapeXml(description)}"">
  <emic-setup>
    <!-- Setup code runs once at initialization -->
  </emic-setup>
  <emic-loop>
    <!-- Main loop code -->
  </emic-loop>
</emic-program>
";
    }

    private static string GenerateUserFncFile(string moduleName, List<string> functions)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"// userFncFile.c - User functions for {moduleName}");
        sb.AppendLine($"#include \"userFncFile.h\"");
        sb.AppendLine();
        sb.AppendLine("// User-defined callback functions");
        sb.AppendLine();

        foreach (var func in functions.Where(f => f.Contains("callback", StringComparison.OrdinalIgnoreCase) ||
                                                   f.Contains("handler", StringComparison.OrdinalIgnoreCase)))
        {
            sb.AppendLine($"void {func}(void) {{");
            sb.AppendLine($"    // TODO: Implement {func}");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string GenerateUserFncHeader(string moduleName, List<string> functions)
    {
        var guard = $"_USERFNCFILE_H_";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"#ifndef {guard}");
        sb.AppendLine($"#define {guard}");
        sb.AppendLine();
        sb.AppendLine($"// userFncFile.h - User function declarations for {moduleName}");
        sb.AppendLine();
        sb.AppendLine("#include <stdint.h>");
        sb.AppendLine();

        foreach (var func in functions.Where(f => f.Contains("callback", StringComparison.OrdinalIgnoreCase) ||
                                                   f.Contains("handler", StringComparison.OrdinalIgnoreCase)))
        {
            sb.AppendLine($"void {func}(void);");
        }

        sb.AppendLine();
        sb.AppendLine($"#endif // {guard}");

        return sb.ToString();
    }

    private static string EscapeXml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
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

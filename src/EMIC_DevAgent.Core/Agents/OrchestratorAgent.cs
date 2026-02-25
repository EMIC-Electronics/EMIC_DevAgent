using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Models.Generation;
using EMIC_DevAgent.Core.Services.Llm;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Agents;

/// <summary>
/// Agente principal que recibe el prompt del usuario, clasifica el intent,
/// realiza desambiguacion con preguntas al usuario, y delega a subagentes.
/// </summary>
public class OrchestratorAgent : AgentBase
{
    private readonly ILlmService _llmService;
    private readonly IUserInteraction _userInteraction;
    private readonly IEnumerable<IAgent> _subAgents;

    public OrchestratorAgent(ILlmService llmService, IUserInteraction userInteraction, IEnumerable<IAgent> subAgents, ILogger<OrchestratorAgent> logger)
        : base(logger)
    {
        _llmService = llmService;
        _userInteraction = userInteraction;
        _subAgents = subAgents;
    }

    public override string Name => "Orchestrator";
    public override string Description => "Coordina el flujo completo: analiza intent, desambigua, delega a subagentes";

    public override bool CanHandle(AgentContext context) => true;

    protected override async Task<AgentResult> ExecuteCoreAsync(AgentContext context, CancellationToken ct)
    {
        // 1. Classify intent from user prompt
        Logger.LogInformation("Classifying intent for prompt: {Prompt}", Truncate(context.OriginalPrompt, 100));

        if (context.Analysis == null)
        {
            context.Analysis = await ClassifyIntentAsync(context.OriginalPrompt, ct);
            Logger.LogInformation("Intent classified: {Intent}, Component: {Component}",
                context.Analysis.Intent, context.Analysis.ComponentName);
        }

        // 2. Disambiguate via questions until fully specified
        var disambiguationAttempts = 0;
        const int maxDisambiguationAttempts = 5;

        while (!IsFullySpecified(context.Analysis) && disambiguationAttempts < maxDisambiguationAttempts)
        {
            ct.ThrowIfCancellationRequested();
            disambiguationAttempts++;

            var question = GenerateDisambiguationQuestion(context.Analysis);
            if (question == null) break;

            context.PendingQuestions.Add(question);

            Logger.LogInformation("Asking disambiguation question: {Question}", question.Question);
            var answer = await _userInteraction.AskQuestionAsync(question, ct);

            question.Answer = answer;
            context.Analysis = RefineAnalysis(context.Analysis, question, answer);

            Logger.LogInformation("Refined analysis after answer: Intent={Intent}, Component={Component}",
                context.Analysis.Intent, context.Analysis.ComponentName);
        }

        // 3. Create generation plan (top-down design)
        if (context.Plan == null)
        {
            context.Plan = CreateGenerationPlan(context.Analysis);
            Logger.LogInformation("Generation plan created: {FileCount} files planned",
                context.Plan.FilesToGenerate.Count);
        }

        // 4. Execute sub-agents bottom-up
        await _userInteraction.ReportProgressAsync(Name, "Starting bottom-up execution...", 0, ct);

        var agentSequence = DetermineAgentSequence(context.Analysis.Intent);
        var totalSteps = agentSequence.Count;

        for (int i = 0; i < agentSequence.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var agentName = agentSequence[i];
            var agent = _subAgents.FirstOrDefault(a => a.Name.Equals(agentName, StringComparison.OrdinalIgnoreCase));

            if (agent == null)
            {
                Logger.LogWarning("Sub-agent '{AgentName}' not found, skipping", agentName);
                continue;
            }

            if (!agent.CanHandle(context))
            {
                Logger.LogDebug("Sub-agent '{AgentName}' cannot handle current context, skipping", agentName);
                continue;
            }

            var progress = (double)(i + 1) / totalSteps * 100;
            await _userInteraction.ReportProgressAsync(Name, $"Running {agentName}...", progress, ct);

            Logger.LogInformation("Executing sub-agent: {AgentName} ({Step}/{Total})", agentName, i + 1, totalSteps);
            var result = await agent.ExecuteAsync(context, ct);

            if (result.Status == ResultStatus.Failure)
            {
                Logger.LogError("Sub-agent '{AgentName}' failed: {Message}", agentName, result.Message);

                // For compilation failures, this is expected — the result is already in context
                if (agentName == "Compilation")
                    return AgentResult.Failure(Name, $"Pipeline stopped: {agentName} failed — {result.Message}");

                // For validator warnings, continue
                if (agentName == "RuleValidator" && !result.Message.Contains("error", StringComparison.OrdinalIgnoreCase))
                    continue;

                return AgentResult.Failure(Name, $"Pipeline stopped: {agentName} failed — {result.Message}");
            }

            if (result.Status == ResultStatus.NeedsInput)
            {
                Logger.LogInformation("Sub-agent '{AgentName}' needs input", agentName);
                return AgentResult.NeedsInput(Name, result.Message);
            }
        }

        await _userInteraction.ReportProgressAsync(Name, "Pipeline completed successfully", 100, ct);

        var fileCount = context.GeneratedFiles.Count;
        var issueCount = context.ValidationResults.Sum(v => v.Issues.Count);
        return AgentResult.Success(Name,
            $"Pipeline completed: {fileCount} files generated" +
            (issueCount > 0 ? $", {issueCount} validation issues" : "") +
            (context.LastCompilation?.Success == true ? ", compilation successful" : ""));
    }

    private async Task<PromptAnalysis> ClassifyIntentAsync(string prompt, CancellationToken ct)
    {
        var llmPrompt = new LlmPromptBuilder()
            .WithSystemInstruction(
                "You are an EMIC SDK intent classifier. Given a user prompt, extract:\n" +
                "intent: one of CreateModule, CreateApi, CreateDriver, ModifyExisting, QueryInfo, Unknown\n" +
                "componentName: name of the component (PascalCase, no spaces)\n" +
                "category: component category (e.g., Sensors, Communication, Actuators, LEDs, Display)\n" +
                "description: one-line description of what the user wants\n" +
                "dependencies: comma-separated list of required APIs/drivers (empty if none)\n" +
                "Respond ONLY with key=value lines, no explanations.")
            .WithUserPrompt(prompt)
            .Build();

        try
        {
            var response = await _llmService.GenerateWithContextAsync(llmPrompt.UserPrompt, llmPrompt.SystemPrompt, ct);
            return ParseIntentResponse(response);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "LLM intent classification failed, using defaults");
            return new PromptAnalysis
            {
                Intent = IntentType.Unknown,
                Description = prompt
            };
        }
    }

    private static PromptAnalysis ParseIntentResponse(string response)
    {
        var analysis = new PromptAnalysis();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in response.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIdx = line.IndexOf('=');
            if (eqIdx <= 0) continue;
            values[line[..eqIdx].Trim()] = line[(eqIdx + 1)..].Trim();
        }

        if (values.TryGetValue("intent", out var intent))
        {
            analysis.Intent = intent switch
            {
                "CreateModule" => IntentType.CreateModule,
                "CreateApi" => IntentType.CreateApi,
                "CreateDriver" => IntentType.CreateDriver,
                "ModifyExisting" => IntentType.ModifyExisting,
                "QueryInfo" => IntentType.QueryInfo,
                _ => IntentType.Unknown
            };
        }

        if (values.TryGetValue("componentName", out var name))
            analysis.ComponentName = name;
        if (values.TryGetValue("category", out var cat))
            analysis.Category = cat;
        if (values.TryGetValue("description", out var desc))
            analysis.Description = desc;
        if (values.TryGetValue("dependencies", out var deps) && !string.IsNullOrWhiteSpace(deps))
        {
            foreach (var dep in deps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                analysis.RequiredDependencies.Add(dep);
        }

        return analysis;
    }

    private static bool IsFullySpecified(PromptAnalysis analysis)
    {
        return analysis.Intent != IntentType.Unknown &&
               !string.IsNullOrWhiteSpace(analysis.ComponentName) &&
               !string.IsNullOrWhiteSpace(analysis.Category);
    }

    private static DisambiguationQuestion? GenerateDisambiguationQuestion(PromptAnalysis analysis)
    {
        if (analysis.Intent == IntentType.Unknown)
        {
            return new DisambiguationQuestion
            {
                Question = "What would you like to create?",
                Options = { "Module", "API", "Driver", "Modify existing component" }
            };
        }

        if (string.IsNullOrWhiteSpace(analysis.ComponentName))
        {
            return new DisambiguationQuestion
            {
                Question = $"What is the name of the {analysis.Intent.ToString().Replace("Create", "")} you want to create?"
            };
        }

        if (string.IsNullOrWhiteSpace(analysis.Category))
        {
            return new DisambiguationQuestion
            {
                Question = $"What category does '{analysis.ComponentName}' belong to?",
                Options = { "Sensors", "Communication", "Actuators", "LEDs", "Display", "General" }
            };
        }

        return null;
    }

    private static PromptAnalysis RefineAnalysis(PromptAnalysis current, DisambiguationQuestion question, string answer)
    {
        if (question.Question.Contains("What would you like to create"))
        {
            current.Intent = answer.ToLowerInvariant() switch
            {
                "module" => IntentType.CreateModule,
                "api" => IntentType.CreateApi,
                "driver" => IntentType.CreateDriver,
                _ when answer.Contains("modify", StringComparison.OrdinalIgnoreCase) => IntentType.ModifyExisting,
                _ => current.Intent
            };
        }
        else if (question.Question.Contains("name of the"))
        {
            current.ComponentName = answer.Trim().Replace(" ", "_");
        }
        else if (question.Question.Contains("category"))
        {
            current.Category = answer.Trim();
        }

        return current;
    }

    private static GenerationPlan CreateGenerationPlan(PromptAnalysis analysis)
    {
        var plan = new GenerationPlan
        {
            ComponentName = analysis.ComponentName,
            ComponentType = analysis.Intent.ToString().Replace("Create", ""),
            Description = analysis.Description,
            RequiresProgramXml = analysis.Intent == IntentType.CreateModule
        };

        switch (analysis.Intent)
        {
            case IntentType.CreateApi:
                plan.FilesToGenerate.Add(new PlannedFile { RelativePath = $"_api/{analysis.Category}/{analysis.ComponentName}/{analysis.ComponentName}.emic", Type = FileType.Emic, Purpose = "API definition" });
                plan.FilesToGenerate.Add(new PlannedFile { RelativePath = $"_api/{analysis.Category}/{analysis.ComponentName}/inc/{analysis.ComponentName}.h", Type = FileType.Header, Purpose = "API header" });
                plan.FilesToGenerate.Add(new PlannedFile { RelativePath = $"_api/{analysis.Category}/{analysis.ComponentName}/src/{analysis.ComponentName}.c", Type = FileType.Source, Purpose = "API source" });
                break;

            case IntentType.CreateDriver:
                plan.FilesToGenerate.Add(new PlannedFile { RelativePath = $"_drivers/{analysis.Category}/{analysis.ComponentName}/{analysis.ComponentName}.emic", Type = FileType.Emic, Purpose = "Driver definition" });
                plan.FilesToGenerate.Add(new PlannedFile { RelativePath = $"_drivers/{analysis.Category}/{analysis.ComponentName}/inc/{analysis.ComponentName}.h", Type = FileType.Header, Purpose = "Driver header" });
                plan.FilesToGenerate.Add(new PlannedFile { RelativePath = $"_drivers/{analysis.Category}/{analysis.ComponentName}/src/{analysis.ComponentName}.c", Type = FileType.Source, Purpose = "Driver source" });
                break;

            case IntentType.CreateModule:
                plan.FilesToGenerate.Add(new PlannedFile { RelativePath = $"_modules/{analysis.Category}/{analysis.ComponentName}/System/generate.emic", Type = FileType.Emic, Purpose = "Generation script" });
                plan.FilesToGenerate.Add(new PlannedFile { RelativePath = $"_modules/{analysis.Category}/{analysis.ComponentName}/System/deploy.emic", Type = FileType.Emic, Purpose = "Deployment script" });
                plan.FilesToGenerate.Add(new PlannedFile { RelativePath = $"_modules/{analysis.Category}/{analysis.ComponentName}/m_description.json", Type = FileType.Json, Purpose = "Module metadata" });
                break;
        }

        foreach (var dep in analysis.RequiredDependencies)
            plan.DependenciesToResolve.Add(dep);

        return plan;
    }

    private static List<string> DetermineAgentSequence(IntentType intent)
    {
        // Bottom-up order: analyze first, then generate from lowest layer up, validate, compile
        return intent switch
        {
            IntentType.CreateModule => new List<string>
            {
                "Analyzer",
                "DriverGenerator",
                "ApiGenerator",
                "ModuleGenerator",
                "ProgramXml",
                "RuleValidator",
                "Compilation"
            },
            IntentType.CreateApi => new List<string>
            {
                "Analyzer",
                "ApiGenerator",
                "RuleValidator",
                "Compilation"
            },
            IntentType.CreateDriver => new List<string>
            {
                "Analyzer",
                "DriverGenerator",
                "RuleValidator",
                "Compilation"
            },
            _ => new List<string>
            {
                "Analyzer",
                "RuleValidator"
            }
        };
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}

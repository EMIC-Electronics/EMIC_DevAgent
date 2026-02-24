using EMIC_DevAgent.Core.Agents.Base;
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

    protected override Task<AgentResult> ExecuteCoreAsync(AgentContext context, CancellationToken ct)
    {
        throw new NotImplementedException("ModuleGeneratorAgent.ExecuteCoreAsync pendiente de implementacion");
    }
}

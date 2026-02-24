using EMIC_DevAgent.Core.Agents.Base;
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

    protected override Task<AgentResult> ExecuteCoreAsync(AgentContext context, CancellationToken ct)
    {
        throw new NotImplementedException("ApiGeneratorAgent.ExecuteCoreAsync pendiente de implementacion");
    }
}

using EMIC_DevAgent.Core.Agents.Base;
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

    protected override Task<AgentResult> ExecuteCoreAsync(AgentContext context, CancellationToken ct)
    {
        throw new NotImplementedException("DriverGeneratorAgent.ExecuteCoreAsync pendiente de implementacion");
    }
}

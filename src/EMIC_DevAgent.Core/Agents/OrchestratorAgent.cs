using EMIC_DevAgent.Core.Agents.Base;
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

    protected override Task<AgentResult> ExecuteCoreAsync(AgentContext context, CancellationToken ct)
    {
        throw new NotImplementedException("OrchestratorAgent.ExecuteCoreAsync pendiente de implementacion");
    }
}

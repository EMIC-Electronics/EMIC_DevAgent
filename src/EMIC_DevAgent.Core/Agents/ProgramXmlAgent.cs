using EMIC_DevAgent.Core.Agents.Base;
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

    protected override Task<AgentResult> ExecuteCoreAsync(AgentContext context, CancellationToken ct)
    {
        throw new NotImplementedException("ProgramXmlAgent.ExecuteCoreAsync pendiente de implementacion");
    }
}

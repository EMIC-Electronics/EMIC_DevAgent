using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Configuration;
using EMIC_DevAgent.Core.Services.Compilation;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Agents;

/// <summary>
/// Compila con XC16, parsea errores, retropropaga correcciones.
/// Reintenta hasta MaxCompilationRetries veces.
/// </summary>
public class CompilationAgent : AgentBase
{
    private readonly ICompilationService _compilationService;
    private readonly EmicAgentConfig _config;

    public CompilationAgent(ICompilationService compilationService, EmicAgentConfig config, ILogger<CompilationAgent> logger)
        : base(logger)
    {
        _compilationService = compilationService;
        _config = config;
    }

    public override string Name => "Compilation";
    public override string Description => "Compila con XC16, parsea errores, retropropaga correcciones";

    public override bool CanHandle(AgentContext context)
        => context.GeneratedFiles.Count > 0;

    protected override Task<AgentResult> ExecuteCoreAsync(AgentContext context, CancellationToken ct)
    {
        throw new NotImplementedException("CompilationAgent.ExecuteCoreAsync pendiente de implementacion");
    }
}

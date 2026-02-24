using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Services.Sdk;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Agents;

/// <summary>
/// Escanea el SDK (_api, _drivers, _modules, _hal), encuentra componentes
/// reutilizables e identifica gaps que necesitan ser creados.
/// </summary>
public class AnalyzerAgent : AgentBase
{
    private readonly ISdkScanner _sdkScanner;

    public AnalyzerAgent(ISdkScanner sdkScanner, ILogger<AnalyzerAgent> logger)
        : base(logger)
    {
        _sdkScanner = sdkScanner;
    }

    public override string Name => "Analyzer";
    public override string Description => "Escanea SDK, encuentra componentes reutilizables, identifica gaps";

    public override bool CanHandle(AgentContext context)
        => context.Analysis != null;

    protected override Task<AgentResult> ExecuteCoreAsync(AgentContext context, CancellationToken ct)
    {
        throw new NotImplementedException("AnalyzerAgent.ExecuteCoreAsync pendiente de implementacion");
    }
}

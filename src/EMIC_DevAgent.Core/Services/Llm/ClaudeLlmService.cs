using EMIC_DevAgent.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Services.Llm;

public class ClaudeLlmService : ILlmService
{
    private readonly EmicAgentConfig _config;
    private readonly ILogger<ClaudeLlmService> _logger;

    public ClaudeLlmService(EmicAgentConfig config, ILogger<ClaudeLlmService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        throw new NotImplementedException("ClaudeLlmService.GenerateAsync pendiente de implementacion");
    }

    public Task<string> GenerateWithContextAsync(string prompt, string context, CancellationToken ct = default)
    {
        throw new NotImplementedException("ClaudeLlmService.GenerateWithContextAsync pendiente de implementacion");
    }

    public Task<T> GenerateStructuredAsync<T>(string prompt, CancellationToken ct = default) where T : class
    {
        throw new NotImplementedException("ClaudeLlmService.GenerateStructuredAsync pendiente de implementacion");
    }
}

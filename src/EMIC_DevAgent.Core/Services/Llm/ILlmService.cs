namespace EMIC_DevAgent.Core.Services.Llm;

public interface ILlmService
{
    Task<string> GenerateAsync(string prompt, CancellationToken ct = default);
    Task<string> GenerateWithContextAsync(string prompt, string context, CancellationToken ct = default);
    Task<T> GenerateStructuredAsync<T>(string prompt, CancellationToken ct = default) where T : class;
}

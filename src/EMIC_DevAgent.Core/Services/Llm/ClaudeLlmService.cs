using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EMIC_DevAgent.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Services.Llm;

public class ClaudeLlmService : ILlmService
{
    private readonly EmicAgentConfig _config;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ClaudeLlmService> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ClaudeLlmService(EmicAgentConfig config, HttpClient httpClient, ILogger<ClaudeLlmService> logger)
    {
        _config = config;
        _httpClient = httpClient;
        _logger = logger;

        _httpClient.BaseAddress ??= new Uri("https://api.anthropic.com/");
        if (!_httpClient.DefaultRequestHeaders.Contains("x-api-key"))
        {
            _httpClient.DefaultRequestHeaders.Add("x-api-key", config.Llm.GetApiKey());
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        }
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        var request = new AnthropicRequest
        {
            Model = _config.Llm.Model,
            MaxTokens = _config.Llm.MaxTokens,
            Temperature = _config.Llm.Temperature,
            Messages = [new AnthropicMessage { Role = "user", Content = prompt }]
        };
        return await SendRequestAsync(request, ct);
    }

    public async Task<string> GenerateWithContextAsync(string prompt, string context, CancellationToken ct = default)
    {
        var request = new AnthropicRequest
        {
            Model = _config.Llm.Model,
            MaxTokens = _config.Llm.MaxTokens,
            Temperature = _config.Llm.Temperature,
            System = context,
            Messages = [new AnthropicMessage { Role = "user", Content = prompt }]
        };
        return await SendRequestAsync(request, ct);
    }

    public async Task<T> GenerateStructuredAsync<T>(string prompt, CancellationToken ct = default) where T : class
    {
        var wrappedPrompt = prompt + "\n\nResponde EXCLUSIVAMENTE con JSON valido, sin markdown ni texto adicional.";
        var json = await GenerateAsync(wrappedPrompt, ct);

        json = json.Trim();
        if (json.StartsWith("```"))
        {
            var firstNewline = json.IndexOf('\n');
            if (firstNewline >= 0)
                json = json[(firstNewline + 1)..];
            var lastFence = json.LastIndexOf("```");
            if (lastFence >= 0)
                json = json[..lastFence];
            json = json.Trim();
        }

        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("LLM retorno JSON null");
    }

    private async Task<string> SendRequestAsync(AnthropicRequest request, CancellationToken ct)
    {
        var jsonContent = JsonSerializer.Serialize(request, _jsonOptions);
        _logger.LogDebug("Anthropic request: model={Model}, maxTokens={MaxTokens}", request.Model, request.MaxTokens);

        using var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("v1/messages", httpContent, ct);

        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Anthropic API error {StatusCode}: {Body}", response.StatusCode, responseBody);
            throw new HttpRequestException($"Anthropic API error {response.StatusCode}: {responseBody}");
        }

        var apiResponse = JsonSerializer.Deserialize<AnthropicResponse>(responseBody, _jsonOptions);
        var text = apiResponse?.Content?.FirstOrDefault(c => c.Type == "text")?.Text;

        if (text == null)
            throw new InvalidOperationException("Respuesta sin contenido de texto");

        _logger.LogDebug("Anthropic response: {InputTokens} input, {OutputTokens} output tokens",
            apiResponse?.Usage?.InputTokens, apiResponse?.Usage?.OutputTokens);

        return text;
    }

    #region Anthropic API Models

    private class AnthropicRequest
    {
        public string Model { get; set; } = string.Empty;
        public int MaxTokens { get; set; }
        public double Temperature { get; set; }
        public string? System { get; set; }
        public List<AnthropicMessage> Messages { get; set; } = new();
    }

    private class AnthropicMessage
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    private class AnthropicResponse
    {
        public string? Id { get; set; }
        public List<AnthropicContent>? Content { get; set; }
        public string? StopReason { get; set; }
        public AnthropicUsage? Usage { get; set; }
    }

    private class AnthropicContent
    {
        public string Type { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

    private class AnthropicUsage
    {
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
    }

    #endregion
}

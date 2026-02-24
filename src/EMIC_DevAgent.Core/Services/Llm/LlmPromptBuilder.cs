namespace EMIC_DevAgent.Core.Services.Llm;

public class LlmPromptBuilder
{
    private readonly List<string> _systemParts = new();
    private readonly List<string> _contextParts = new();
    private string _userPrompt = string.Empty;

    public LlmPromptBuilder WithSystemInstruction(string instruction)
    {
        _systemParts.Add(instruction);
        return this;
    }

    public LlmPromptBuilder WithContext(string context)
    {
        _contextParts.Add(context);
        return this;
    }

    public LlmPromptBuilder WithUserPrompt(string prompt)
    {
        _userPrompt = prompt;
        return this;
    }

    public LlmPrompt Build()
    {
        var systemPrompt = string.Join("\n\n",
            _systemParts.Concat(_contextParts).Where(s => !string.IsNullOrWhiteSpace(s)));
        return new LlmPrompt(systemPrompt, _userPrompt);
    }

    public LlmPromptBuilder Clear()
    {
        _systemParts.Clear();
        _contextParts.Clear();
        _userPrompt = string.Empty;
        return this;
    }
}

public record LlmPrompt(string SystemPrompt, string UserPrompt);

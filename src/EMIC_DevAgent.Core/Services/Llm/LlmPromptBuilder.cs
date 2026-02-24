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

    public string Build()
    {
        throw new NotImplementedException("LlmPromptBuilder.Build pendiente de implementacion");
    }
}

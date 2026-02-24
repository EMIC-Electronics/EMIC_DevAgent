using EMIC_DevAgent.Core.Models.Generation;
using EMIC_DevAgent.Core.Models.Sdk;
using EMIC_DevAgent.Core.Services.Validation;

namespace EMIC_DevAgent.Core.Agents.Base;

public class AgentContext
{
    public string OriginalPrompt { get; set; } = string.Empty;
    public PromptAnalysis? Analysis { get; set; }
    public SdkInventory? SdkState { get; set; }
    public GenerationPlan? Plan { get; set; }
    public List<GeneratedFile> GeneratedFiles { get; } = new();
    public List<DisambiguationQuestion> PendingQuestions { get; } = new();
    public List<ValidationResult> ValidationResults { get; } = new();
    public CompilationResult? LastCompilation { get; set; }
    public Dictionary<string, object> Properties { get; } = new();
}

public class PromptAnalysis
{
    public IntentType Intent { get; set; }
    public string ComponentName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> RequiredDependencies { get; } = new();
}

public enum IntentType
{
    CreateModule,
    CreateApi,
    CreateDriver,
    ModifyExisting,
    QueryInfo,
    Unknown
}

public class DisambiguationQuestion
{
    public string Question { get; set; } = string.Empty;
    public List<string> Options { get; } = new();
    public string? Answer { get; set; }
}

public class CompilationResult
{
    public bool Success { get; set; }
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public int AttemptNumber { get; set; }
}

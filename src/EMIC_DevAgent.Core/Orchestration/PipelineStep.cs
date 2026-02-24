using EMIC_DevAgent.Core.Agents.Base;

namespace EMIC_DevAgent.Core.Orchestration;

public class PipelineStep
{
    public string Name { get; set; } = string.Empty;
    public IAgent Agent { get; set; } = null!;
    public Func<AgentContext, bool>? Condition { get; set; }
    public int Order { get; set; }
    public StepStatus Status { get; set; } = StepStatus.Pending;
    public AgentResult? Result { get; set; }
}

public enum StepStatus
{
    Pending,
    Running,
    Completed,
    Skipped,
    Failed
}

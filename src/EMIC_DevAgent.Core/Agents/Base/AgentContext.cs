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
    public DetailedSpecification? Specification { get; set; }
    public bool DisambiguationOnly { get; set; }
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

public enum ProjectType
{
    Unknown,
    Monolithic,
    EmicModule,
    DistributedSystem
}

public enum ModuleRole
{
    Unknown,
    Sensor,
    Actuator,
    Display,
    Input,
    Indicator,
    Communication,
    Other
}

public enum SystemKind
{
    Unknown,
    ClosedLoopControl,
    Monitoring,
    RemoteControl,
    Combined,
    Other
}

public enum DeviceFunction
{
    Unknown,
    Controller,
    Datalogger,
    RemoteMonitor,
    Gateway,
    Hmi,
    MultiFunction,
    Other
}

public enum ApiType
{
    Unknown,
    SignalProcessing,
    CommunicationProtocol,
    Storage,
    UserInterface,
    Control,
    Other
}

public enum DriverTarget
{
    Unknown,
    Sensor,
    Converter,
    Memory,
    Display,
    Transceiver,
    MotorActuator,
    Other
}

public class DetailedSpecification
{
    public ProjectType ProjectType { get; set; }
    public ModuleRole ModuleRole { get; set; }
    public SystemKind SystemKind { get; set; }
    public DeviceFunction DeviceFunction { get; set; }
    public ApiType ApiType { get; set; }
    public DriverTarget DriverTarget { get; set; }
    public IntentType Intent { get; set; }
    public string ComponentName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SensorType { get; set; } = string.Empty;
    public string CommunicationInterface { get; set; } = string.Empty;
    public string MeasurementRange { get; set; } = string.Empty;
    public string MeasurementUnit { get; set; } = string.Empty;
    public string Precision { get; set; } = string.Empty;
    public string TargetPcb { get; set; } = string.Empty;
    public string ChipOrProtocol { get; set; } = string.Empty;
    public string OutputType { get; set; } = string.Empty;
    public List<string> ReusableApis { get; } = new();
    public List<string> ReusableDrivers { get; } = new();
    public List<string> ComponentsToCreate { get; } = new();
    public List<string> RequiredDependencies { get; } = new();
    public Dictionary<string, string> AdditionalDetails { get; } = new();
    public List<DisambiguationExchange> ConversationHistory { get; } = new();
}

public class DisambiguationExchange
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public List<string> Options { get; } = new();
}

public class CompilationResult
{
    public bool Success { get; set; }
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public int AttemptNumber { get; set; }
}

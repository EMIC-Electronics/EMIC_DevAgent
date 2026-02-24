using System.Text.Json.Serialization;

namespace EMIC_DevAgent.Core.Models.Metadata;

/// <summary>
/// Modelo para .emic-meta.json en cada carpeta del SDK.
/// </summary>
public class FolderMetadata
{
    [JsonPropertyName("$schema")]
    public string Schema { get; set; } = "emic-metadata-v1";

    [JsonPropertyName("component")]
    public ComponentInfo Component { get; set; } = new();

    [JsonPropertyName("relationships")]
    public RelationshipInfo Relationships { get; set; } = new();

    [JsonPropertyName("quality")]
    public QualityInfo Quality { get; set; } = new();

    [JsonPropertyName("history")]
    public List<HistoryEntry> History { get; } = new();
}

public class ComponentInfo
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
}

public class RelationshipInfo
{
    [JsonPropertyName("dependsOn")]
    public List<DependencyRef> DependsOn { get; } = new();

    [JsonPropertyName("usedBy")]
    public List<DependencyRef> UsedBy { get; } = new();

    [JsonPropertyName("provides")]
    public ProvidesInfo Provides { get; set; } = new();
}

public class DependencyRef
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
}

public class ProvidesInfo
{
    [JsonPropertyName("functions")]
    public List<string> Functions { get; } = new();

    [JsonPropertyName("dictionaries")]
    public Dictionary<string, string> Dictionaries { get; } = new();
}

public class QualityInfo
{
    [JsonPropertyName("compilation")]
    public string Compilation { get; set; } = "not_attempted";

    [JsonPropertyName("ruleValidation")]
    public Dictionary<string, string> RuleValidation { get; } = new();

    [JsonPropertyName("debugState")]
    public string DebugState { get; set; } = "in_progress";
}

public class HistoryEntry
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("agent")]
    public string Agent { get; set; } = string.Empty;
}

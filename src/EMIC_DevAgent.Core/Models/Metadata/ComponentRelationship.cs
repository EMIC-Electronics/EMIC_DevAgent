namespace EMIC_DevAgent.Core.Models.Metadata;

public class ComponentRelationship
{
    public string SourceComponent { get; set; } = string.Empty;
    public string TargetComponent { get; set; } = string.Empty;
    public RelationshipType Type { get; set; }
}

public enum RelationshipType
{
    DependsOn,
    UsedBy,
    Provides,
    Includes
}

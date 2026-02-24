namespace EMIC_DevAgent.Core.Services.Validation;

public class ValidationResult
{
    public string ValidatorName { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public List<ValidationIssue> Issues { get; } = new();
}

public class ValidationIssue
{
    public string FilePath { get; set; } = string.Empty;
    public int Line { get; set; }
    public string Rule { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public IssueSeverity Severity { get; set; }
}

public enum IssueSeverity
{
    Warning,
    Error
}

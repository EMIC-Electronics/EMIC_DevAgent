namespace EMIC_DevAgent.Core.Agents.Base;

public interface IUserInteraction
{
    Task<string> AskQuestionAsync(DisambiguationQuestion question, CancellationToken ct = default);
    Task ReportProgressAsync(string agentName, string message, double? progressPercent = null, CancellationToken ct = default);
    Task<bool> ConfirmActionAsync(string description, CancellationToken ct = default);
}

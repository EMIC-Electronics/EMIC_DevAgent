using EMIC_DevAgent.Core.Models.Generation;

namespace EMIC_DevAgent.Core.Services.Templates;

public interface ITemplateEngine
{
    Task<GeneratedFile> GenerateFromTemplateAsync(string templateName, Dictionary<string, string> variables, CancellationToken ct = default);
    string ApplyVariables(string template, Dictionary<string, string> variables);
}

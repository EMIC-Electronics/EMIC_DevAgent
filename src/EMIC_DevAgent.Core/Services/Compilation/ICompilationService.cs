using EMIC_DevAgent.Core.Agents.Base;

namespace EMIC_DevAgent.Core.Services.Compilation;

public interface ICompilationService
{
    Task<CompilationResult> CompileAsync(string projectPath, CancellationToken ct = default);
}

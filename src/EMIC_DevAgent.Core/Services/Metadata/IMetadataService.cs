using EMIC_DevAgent.Core.Models.Metadata;

namespace EMIC_DevAgent.Core.Services.Metadata;

public interface IMetadataService
{
    Task<FolderMetadata?> ReadMetadataAsync(string folderPath, CancellationToken ct = default);
    Task WriteMetadataAsync(string folderPath, FolderMetadata metadata, CancellationToken ct = default);
    Task UpdateHistoryAsync(string folderPath, string action, string agentName, CancellationToken ct = default);
}

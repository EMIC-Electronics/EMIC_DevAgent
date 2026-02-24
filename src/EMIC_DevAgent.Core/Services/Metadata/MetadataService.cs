using EMIC_DevAgent.Core.Models.Metadata;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Services.Metadata;

public class MetadataService : IMetadataService
{
    private const string MetadataFileName = ".emic-meta.json";
    private readonly ILogger<MetadataService> _logger;

    public MetadataService(ILogger<MetadataService> logger)
    {
        _logger = logger;
    }

    public Task<FolderMetadata?> ReadMetadataAsync(string folderPath, CancellationToken ct = default)
    {
        throw new NotImplementedException("MetadataService.ReadMetadataAsync pendiente de implementacion");
    }

    public Task WriteMetadataAsync(string folderPath, FolderMetadata metadata, CancellationToken ct = default)
    {
        throw new NotImplementedException("MetadataService.WriteMetadataAsync pendiente de implementacion");
    }

    public Task UpdateHistoryAsync(string folderPath, string action, string agentName, CancellationToken ct = default)
    {
        throw new NotImplementedException("MetadataService.UpdateHistoryAsync pendiente de implementacion");
    }
}

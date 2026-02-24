using System.Text.Json;
using EMIC.Shared.Services.Storage;
using EMIC_DevAgent.Core.Models.Metadata;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Services.Metadata;

public class MetadataService : IMetadataService
{
    private const string MetadataFileName = ".emic-meta.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly MediaAccess _mediaAccess;
    private readonly ILogger<MetadataService> _logger;

    public MetadataService(MediaAccess mediaAccess, ILogger<MetadataService> logger)
    {
        _mediaAccess = mediaAccess;
        _logger = logger;
    }

    public Task<FolderMetadata?> ReadMetadataAsync(string folderPath, CancellationToken ct = default)
    {
        var metaPath = BuildMetaPath(folderPath);

        if (!_mediaAccess.File.Exists(metaPath))
        {
            _logger.LogDebug("No metadata file at {MetaPath}", metaPath);
            return Task.FromResult<FolderMetadata?>(null);
        }

        try
        {
            var json = _mediaAccess.File.ReadAllText(metaPath);
            var metadata = JsonSerializer.Deserialize<FolderMetadata>(json, JsonOptions);
            return Task.FromResult(metadata);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read metadata from {MetaPath}", metaPath);
            return Task.FromResult<FolderMetadata?>(null);
        }
    }

    public Task WriteMetadataAsync(string folderPath, FolderMetadata metadata, CancellationToken ct = default)
    {
        var metaPath = BuildMetaPath(folderPath);
        var json = JsonSerializer.Serialize(metadata, JsonOptions);
        _mediaAccess.File.WriteAllText(metaPath, json);
        _logger.LogDebug("Wrote metadata to {MetaPath}", metaPath);
        return Task.CompletedTask;
    }

    public async Task UpdateHistoryAsync(string folderPath, string action, string agentName, CancellationToken ct = default)
    {
        var metadata = await ReadMetadataAsync(folderPath, ct) ?? new FolderMetadata();

        metadata.History.Add(new HistoryEntry
        {
            Date = DateTime.UtcNow.ToString("o"),
            Action = action,
            Agent = agentName
        });

        await WriteMetadataAsync(folderPath, metadata, ct);
    }

    private static string BuildMetaPath(string folderPath)
        => folderPath.TrimEnd('/') + "/" + MetadataFileName;
}

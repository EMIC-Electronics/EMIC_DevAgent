using System.Text.RegularExpressions;
using EMIC_DevAgent.Core.Models.Generation;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Services.Compilation;

/// <summary>
/// Maps compilation errors in expanded TARGET code back to SDK source files.
/// Inserts @source markers into generated code and resolves error locations.
/// </summary>
public class SourceMapper
{
    private readonly ILogger<SourceMapper> _logger;

    // Marker format: // @source: _api/Indicators/LEDs/src/led.c:42
    private static readonly Regex SourceMarkerRegex = new(
        @"^//\s*@source:\s*(.+?):(\d+)\s*$",
        RegexOptions.Compiled);

    public SourceMapper(ILogger<SourceMapper> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Inserts @source markers into a generated file's content.
    /// Each line gets a marker referencing the original source file and line.
    /// Markers are inserted as comments before each code block.
    /// </summary>
    public string InsertMarkers(GeneratedFile file)
    {
        if (file.Type == FileType.Emic || file.Type == FileType.Json || file.Type == FileType.Xml)
            return file.Content;

        var lines = file.Content.Split('\n');
        var result = new List<string>();

        // Insert a block marker at the beginning referencing the source
        result.Add($"// @source: {file.RelativePath}:1");

        int blockSize = 10;
        for (int i = 0; i < lines.Length; i++)
        {
            // Insert markers every blockSize lines for efficient lookup
            if (i > 0 && i % blockSize == 0)
            {
                result.Add($"// @source: {file.RelativePath}:{i + 1}");
            }
            result.Add(lines[i]);
        }

        return string.Join('\n', result);
    }

    /// <summary>
    /// Resolves a compilation error's TARGET file + line to the original SDK source file + line.
    /// Scans upward from the error line looking for the nearest @source marker.
    /// </summary>
    public SourceMapping? ResolveErrorLocation(string expandedContent, int errorLine)
    {
        var lines = expandedContent.Split('\n');

        if (errorLine < 1 || errorLine > lines.Length)
            return null;

        // Search upward from error line for nearest @source marker
        for (int i = errorLine - 1; i >= 0; i--)
        {
            var match = SourceMarkerRegex.Match(lines[i].Trim());
            if (match.Success)
            {
                var sourceFile = match.Groups[1].Value;
                var sourceLine = int.Parse(match.Groups[2].Value);
                var offset = errorLine - 1 - i; // lines between marker and error

                return new SourceMapping
                {
                    OriginalFilePath = sourceFile,
                    OriginalLine = sourceLine + offset - 1, // -1 because marker line itself isn't in original
                    ExpandedLine = errorLine
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Maps a CompilationError to the original generated file using @source markers.
    /// Returns the GeneratedFile and the mapped line number within it.
    /// </summary>
    public SourceMappedError? MapError(CompilationError error, List<GeneratedFile> generatedFiles, string? expandedContent = null)
    {
        // Strategy 1: If we have expanded content with @source markers, use those
        if (expandedContent != null && error.Line > 0)
        {
            var mapping = ResolveErrorLocation(expandedContent, error.Line);
            if (mapping != null)
            {
                var matchedFile = generatedFiles.FirstOrDefault(f =>
                    f.RelativePath.Equals(mapping.OriginalFilePath, StringComparison.OrdinalIgnoreCase) ||
                    mapping.OriginalFilePath.EndsWith(f.RelativePath, StringComparison.OrdinalIgnoreCase) ||
                    f.RelativePath.EndsWith(mapping.OriginalFilePath, StringComparison.OrdinalIgnoreCase));

                if (matchedFile != null)
                {
                    _logger.LogDebug("Mapped error at expanded:{ExpandedLine} -> {File}:{Line}",
                        error.Line, matchedFile.RelativePath, mapping.OriginalLine);

                    return new SourceMappedError
                    {
                        OriginalError = error,
                        MappedFile = matchedFile,
                        MappedLine = mapping.OriginalLine
                    };
                }
            }
        }

        // Strategy 2: Fallback to filename matching
        var errorFileName = Path.GetFileName(error.FilePath);
        if (!string.IsNullOrEmpty(errorFileName))
        {
            var fileByName = generatedFiles.FirstOrDefault(f =>
                (f.Type == FileType.Source || f.Type == FileType.Header) &&
                Path.GetFileName(f.RelativePath).Equals(errorFileName, StringComparison.OrdinalIgnoreCase));

            if (fileByName != null)
            {
                _logger.LogDebug("Mapped error by filename: {ErrorFile} -> {File}:{Line}",
                    error.FilePath, fileByName.RelativePath, error.Line);

                return new SourceMappedError
                {
                    OriginalError = error,
                    MappedFile = fileByName,
                    MappedLine = error.Line
                };
            }
        }

        _logger.LogDebug("Could not map error in {File}:{Line}", error.FilePath, error.Line);
        return null;
    }
}

public class SourceMapping
{
    public string OriginalFilePath { get; set; } = string.Empty;
    public int OriginalLine { get; set; }
    public int ExpandedLine { get; set; }
}

public class SourceMappedError
{
    public CompilationError OriginalError { get; set; } = null!;
    public GeneratedFile MappedFile { get; set; } = null!;
    public int MappedLine { get; set; }
}

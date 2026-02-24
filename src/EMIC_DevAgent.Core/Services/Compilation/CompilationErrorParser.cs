using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Services.Compilation;

/// <summary>
/// Parsea la salida del compilador XC16/XC8/GCC para extraer errores y warnings estructurados.
/// </summary>
public class CompilationErrorParser
{
    private readonly ILogger<CompilationErrorParser> _logger;

    // XC16/GCC format: file.c:42:10: error: undeclared identifier 'x'
    // XC16 format alt: file.c:42: error: (1098) message
    private static readonly Regex GccPattern = new(
        @"^(.+?):(\d+):(?:(\d+):)?\s*(error|warning|note|fatal error):\s*(?:\((\d+)\)\s*)?(.+)$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // Linker errors: "xxx.o(.text+0x1a): In function `main': undefined reference to `foo'"
    private static readonly Regex LinkerPattern = new(
        @"^(.+?\.o)\(.*?\):\s*(?:In function `.+?':)?\s*(.+)$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public CompilationErrorParser(ILogger<CompilationErrorParser> logger)
    {
        _logger = logger;
    }

    public List<CompilationError> Parse(string compilerOutput)
    {
        var errors = new List<CompilationError>();
        if (string.IsNullOrWhiteSpace(compilerOutput)) return errors;

        foreach (Match match in GccPattern.Matches(compilerOutput))
        {
            errors.Add(new CompilationError
            {
                FilePath = match.Groups[1].Value.Trim(),
                Line = int.Parse(match.Groups[2].Value),
                Column = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0,
                Severity = match.Groups[4].Value,
                Code = match.Groups[5].Success ? match.Groups[5].Value : "",
                Message = match.Groups[6].Value.Trim()
            });
        }

        foreach (Match match in LinkerPattern.Matches(compilerOutput))
        {
            errors.Add(new CompilationError
            {
                FilePath = match.Groups[1].Value.Trim(),
                Severity = "error",
                Message = match.Groups[2].Value.Trim()
            });
        }

        _logger.LogDebug("Parsed {Count} errors/warnings from compiler output", errors.Count);
        return errors;
    }
}

public class CompilationError
{
    public string FilePath { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Column { get; set; }
    public string Severity { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

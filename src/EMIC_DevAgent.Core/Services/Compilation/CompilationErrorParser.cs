using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Services.Compilation;

/// <summary>
/// Parsea la salida del compilador XC16 para extraer errores y warnings estructurados.
/// </summary>
public class CompilationErrorParser
{
    private readonly ILogger<CompilationErrorParser> _logger;

    public CompilationErrorParser(ILogger<CompilationErrorParser> logger)
    {
        _logger = logger;
    }

    public List<CompilationError> Parse(string compilerOutput)
    {
        throw new NotImplementedException("CompilationErrorParser.Parse pendiente de implementacion");
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

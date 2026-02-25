using EMIC_DevAgent.Core.Models.Generation;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Services.Templates;

/// <summary>
/// Template para generar drivers (.emic, .h, .c) siguiendo patrones como ADS1231.
/// </summary>
public class DriverTemplate
{
    private readonly ILogger<DriverTemplate> _logger;

    public DriverTemplate(ILogger<DriverTemplate> logger)
    {
        _logger = logger;
    }

    public Task<List<GeneratedFile>> GenerateAsync(string driverName, string chipType, Dictionary<string, string> variables, CancellationToken ct = default)
    {
        _logger.LogInformation("Generating Driver template for {DriverName} (chip: {ChipType})", driverName, chipType);

        var halDependency = variables.GetValueOrDefault("halDependency", "GPIO");
        var category = variables.GetValueOrDefault("category", "Sensors");
        var description = variables.GetValueOrDefault("description", $"{driverName} driver for {chipType}");

        var basePath = $"_drivers/{category}/{driverName}";

        var files = new List<GeneratedFile>
        {
            new()
            {
                RelativePath = $"{basePath}/{driverName}.emic",
                Content = GenerateEmicFile(driverName, halDependency, description),
                Type = FileType.Emic,
                GeneratedByAgent = "DriverTemplate"
            },
            new()
            {
                RelativePath = $"{basePath}/inc/{driverName}.h",
                Content = GenerateHeaderFile(driverName, chipType, description),
                Type = FileType.Header,
                GeneratedByAgent = "DriverTemplate"
            },
            new()
            {
                RelativePath = $"{basePath}/src/{driverName}.c",
                Content = GenerateSourceFile(driverName, chipType),
                Type = FileType.Source,
                GeneratedByAgent = "DriverTemplate"
            }
        };

        _logger.LogInformation("Generated {Count} files for Driver {DriverName}", files.Count, driverName);
        return Task.FromResult(files);
    }

    private static string GenerateEmicFile(string driverName, string halDependency, string description)
    {
        return $@"//EMIC:tag({driverName})
/// @file {driverName}.emic
/// @brief {description}

EMIC:ifndef({driverName}_included)
EMIC:define({driverName}_included,1)

EMIC:setInput(DEV:_hal/{halDependency}/{halDependency.ToLowerInvariant()}.emic)

EMIC:copy(inc/{driverName}.h,TARGET:inc/{driverName}.h)
EMIC:copy(src/{driverName}.c,TARGET:src/{driverName}.c)

EMIC:define(c_modules.{driverName},TARGET:src/{driverName}.c)

EMIC:endif
";
    }

    private static string GenerateHeaderFile(string driverName, string chipType, string description)
    {
        var guardName = $"_{driverName.ToUpperInvariant()}_H_";
        return $@"#ifndef {guardName}
#define {guardName}

/// @file {driverName}.h
/// @brief {description}

#include <stdint.h>

void {driverName}_init(void);
uint8_t {driverName}_read(void);
void {driverName}_write(uint8_t data);

#endif // {guardName}
";
    }

    private static string GenerateSourceFile(string driverName, string chipType)
    {
        return $@"#include ""{driverName}.h""
#include ""hal_gpio.h""

void {driverName}_init(void) {{
    // TODO: Initialize {chipType} hardware
}}

uint8_t {driverName}_read(void) {{
    // TODO: Implement {chipType} read
    return 0;
}}

void {driverName}_write(uint8_t data) {{
    // TODO: Implement {chipType} write
}}
";
    }
}

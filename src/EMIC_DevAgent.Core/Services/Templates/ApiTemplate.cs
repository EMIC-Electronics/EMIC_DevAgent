using EMIC_DevAgent.Core.Models.Generation;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Services.Templates;

/// <summary>
/// Template para generar APIs (.emic, .h, .c) siguiendo patrones como led.emic.
/// </summary>
public class ApiTemplate
{
    private readonly ILogger<ApiTemplate> _logger;

    public ApiTemplate(ILogger<ApiTemplate> logger)
    {
        _logger = logger;
    }

    public Task<List<GeneratedFile>> GenerateAsync(string apiName, string category, Dictionary<string, string> variables, CancellationToken ct = default)
    {
        _logger.LogInformation("Generating API template for {ApiName} in category {Category}", apiName, category);

        var name = variables.GetValueOrDefault("name", apiName);
        var pin = variables.GetValueOrDefault("pin", "PIN_0");
        var driverName = variables.GetValueOrDefault("driverName", apiName.ToLowerInvariant());
        var description = variables.GetValueOrDefault("description", $"{name} API");

        var basePath = $"_api/{category}/{apiName}";

        var files = new List<GeneratedFile>
        {
            new()
            {
                RelativePath = $"{basePath}/{apiName}.emic",
                Content = GenerateEmicFile(apiName, name, driverName, description),
                Type = FileType.Emic,
                GeneratedByAgent = "ApiTemplate"
            },
            new()
            {
                RelativePath = $"{basePath}/inc/{apiName}.h",
                Content = GenerateHeaderFile(apiName, name, pin, description),
                Type = FileType.Header,
                GeneratedByAgent = "ApiTemplate"
            },
            new()
            {
                RelativePath = $"{basePath}/src/{apiName}.c",
                Content = GenerateSourceFile(apiName, name, pin),
                Type = FileType.Source,
                GeneratedByAgent = "ApiTemplate"
            }
        };

        _logger.LogInformation("Generated {Count} files for API {ApiName}", files.Count, apiName);
        return Task.FromResult(files);
    }

    private static string GenerateEmicFile(string apiName, string name, string driverName, string description)
    {
        return $@"//EMIC:tag({apiName})
/// @file {apiName}.emic
/// @brief {description}

/// @fn {name}_init
/// @brief Initializes the {name} API
/// @param none
/// @return void

/// @fn {name}_on
/// @brief Turns {name} on
/// @param none
/// @return void

/// @fn {name}_off
/// @brief Turns {name} off
/// @param none
/// @return void

/// @fn {name}_toggle
/// @brief Toggles {name} state
/// @param none
/// @return void

EMIC:setInput(DEV:_hal/GPIO/gpio.emic)

EMIC:copy(inc/{apiName}.h,TARGET:inc/{apiName}.h)
EMIC:copy(src/{apiName}.c,TARGET:src/{apiName}.c)

EMIC:define(main_includes.{apiName},#include ""{apiName}.h"")
EMIC:define(c_modules.{apiName},TARGET:src/{apiName}.c)
EMIC:define(inits.{apiName},{name}_init)
";
    }

    private static string GenerateHeaderFile(string apiName, string name, string pin, string description)
    {
        var guardName = $"_{apiName.ToUpperInvariant()}_H_";
        return $@"#ifndef {guardName}
#define {guardName}

/// @file {apiName}.h
/// @brief {description}

#include <xc.h>
#include <stdint.h>

void {name}_init(void);

EMIC:ifdef({apiName}.on)
void {name}_on(void);
EMIC:endif

EMIC:ifdef({apiName}.off)
void {name}_off(void);
EMIC:endif

EMIC:ifdef({apiName}.toggle)
void {name}_toggle(void);
EMIC:endif

#endif // {guardName}
";
    }

    private static string GenerateSourceFile(string apiName, string name, string pin)
    {
        return $@"#include ""{apiName}.h""
#include ""hal_gpio.h""

static uint8_t {name}_state = 0;

void {name}_init(void) {{
    HAL_GPIO_PinCfg({pin}, GPIO_OUTPUT);
    HAL_GPIO_WritePin({pin}, 0);
    {name}_state = 0;
}}

EMIC:ifdef({apiName}.on)
void {name}_on(void) {{
    HAL_GPIO_WritePin({pin}, 1);
    {name}_state = 1;
}}
EMIC:endif

EMIC:ifdef({apiName}.off)
void {name}_off(void) {{
    HAL_GPIO_WritePin({pin}, 0);
    {name}_state = 0;
}}
EMIC:endif

EMIC:ifdef({apiName}.toggle)
void {name}_toggle(void) {{
    {name}_state = !{name}_state;
    HAL_GPIO_WritePin({pin}, {name}_state);
}}
EMIC:endif
";
    }
}

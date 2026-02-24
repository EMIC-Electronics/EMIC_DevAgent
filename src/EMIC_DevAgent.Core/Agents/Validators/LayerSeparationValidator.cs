using EMIC_DevAgent.Core.Agents.Base;
using EMIC_DevAgent.Core.Services.Validation;
using Microsoft.Extensions.Logging;

namespace EMIC_DevAgent.Core.Agents.Validators;

/// <summary>
/// Verifica que las APIs no acceden registros directos (TRIS, LAT, PORT).
/// Solo deben usar HAL_GPIO_*, HAL_SPI_*, HAL_I2C_*, etc.
/// </summary>
public class LayerSeparationValidator : IValidator
{
    private readonly ILogger<LayerSeparationValidator> _logger;

    public LayerSeparationValidator(ILogger<LayerSeparationValidator> logger)
    {
        _logger = logger;
    }

    public string Name => "LayerSeparation";
    public string Description => "APIs no acceden registros directos. Solo usan HAL_GPIO_*, HAL_SPI_*, etc.";

    public Task<ValidationResult> ValidateAsync(AgentContext context, CancellationToken ct = default)
    {
        throw new NotImplementedException("LayerSeparationValidator pendiente de implementacion");
    }
}

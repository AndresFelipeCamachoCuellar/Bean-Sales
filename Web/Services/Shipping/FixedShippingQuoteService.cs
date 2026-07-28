using Microsoft.Extensions.Options;

namespace Web.Services.Shipping;

/// <summary>
/// Tarifa plana de configuración (Shipping:FallbackCost, por defecto 18.000 COP).
/// Cumple tres roles:
///  a) es el comportamiento actual del sitio y permite desarrollar sin credenciales;
///  b) será la red de seguridad del cotizador real (se inyecta como fallback);
///  c) desbloquea todo el flujo de checkout antes de que exista la cuenta del proveedor.
/// </summary>
public sealed class FixedShippingQuoteService : IShippingQuoteService
{
    public const string SourceName = "Fixed";
    public const string CarrierCode = "STANDARD";
    public const string CarrierDisplayName = "Envío estándar";

    private readonly ShippingOptions _options;

    public FixedShippingQuoteService(IOptions<ShippingOptions> options)
    {
        _options = options.Value;
    }

    public Task<ShippingQuoteResult> QuoteAsync(ShippingQuoteRequest request, CancellationToken ct = default)
    {
        return Task.FromResult(Build(SourceName, ShippingQuoteStatus.Ok));
    }

    /// <summary>
    /// Construye el resultado de tarifa plana. El cotizador real lo reutilizará con
    /// source = "Fallback" cuando la API falle.
    /// </summary>
    public ShippingQuoteResult Build(string source, ShippingQuoteStatus status, string? message = null)
    {
        var option = new ShippingOption
        {
            CarrierCode = CarrierCode,
            CarrierName = CarrierDisplayName,
            Price = _options.FallbackCost,
            EstimatedDays = _options.FallbackDays
        };

        return new ShippingQuoteResult
        {
            Status = status,
            Options = new[] { option },
            Source = source,
            Message = message
        };
    }
}

namespace Web.Services.Shipping;

// ---------- Entrada ----------

/// <summary>Todo lo que una transportadora necesita para cotizar un envío nacional.</summary>
public sealed record ShippingQuoteRequest
{
    public required string OriginDaneCode { get; init; }
    public required string DestinationDaneCode { get; init; }
    public required decimal WeightKg { get; init; }
    public required decimal LengthCm { get; init; }
    public required decimal WidthCm { get; init; }
    public required decimal HeightCm { get; init; }

    /// <summary>Valor declarado en COP (subtotal del carrito). Afecta el seguro y el precio.</summary>
    public required decimal DeclaredValue { get; init; }

    public string CountryCode { get; init; } = "CO";
}

// ---------- Salida ----------

public sealed record ShippingOption
{
    public required string CarrierCode { get; init; }
    public required string CarrierName { get; init; }

    /// <summary>Precio en COP, ya redondeado.</summary>
    public required decimal Price { get; init; }

    public required int EstimatedDays { get; init; }

    /// <summary>Calificación del transportador, si el proveedor la expone.</summary>
    public decimal? Rating { get; init; }

    /// <summary>Id externo de la transportadora, necesario para generar la guía más adelante.</summary>
    public string? CarrierExternalId { get; init; }
}

public enum ShippingQuoteStatus
{
    /// <summary>Cotización real.</summary>
    Ok,

    /// <summary>La API falló: se aplicó la tarifa de respaldo. La venta continúa.</summary>
    Fallback,

    /// <summary>No hay transportadora para ese destino. Aquí SÍ se bloquea la venta.</summary>
    NoCoverage,

    /// <summary>Input inválido (sin DANE, peso 0, peso por encima del máximo…).</summary>
    Invalid
}

public sealed record ShippingQuoteResult
{
    public required ShippingQuoteStatus Status { get; init; }
    public required IReadOnlyList<ShippingOption> Options { get; init; }

    /// <summary>"Mipaquete" | "Fixed" | "Fallback". Se persiste en Order.ShippingQuoteSource.</summary>
    public required string Source { get; init; }

    /// <summary>Mensaje ya en español, listo para mostrar en la vista.</summary>
    public string? Message { get; init; }

    public ShippingOption? Cheapest => Options.OrderBy(o => o.Price).FirstOrDefault();
}

// ---------- Interfaz ----------

/// <summary>
/// Cotizador de envío. Las implementaciones NUNCA lanzan por fallo de la API:
/// los errores de red se traducen a <see cref="ShippingQuoteStatus.Fallback"/>.
/// </summary>
public interface IShippingQuoteService
{
    Task<ShippingQuoteResult> QuoteAsync(ShippingQuoteRequest request, CancellationToken ct = default);
}

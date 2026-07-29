using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Web.Models;
using Web.Models.Enums;

namespace Web.Services.Payments;

/// <summary>
/// Implementación de <see cref="IPaymentGateway"/> sobre Wompi (Bancolombia), usando
/// **Web Checkout (redirección)**: un formulario HTML estándar hacia checkout.wompi.co.
///
/// Reglas no negociables:
///  · La firma de integridad se calcula SIEMPRE aquí (servidor). El IntegritySecret
///    jamás se sirve a una vista ni a JavaScript.
///  · <see cref="GetTransactionAsync"/> NUNCA lanza: si la consulta falla se devuelve
///    null y la verdad la pone el webhook (misma filosofía que el fallback de envío).
///  · Ningún secreto se escribe en logs.
///
/// ⚠️ NO CONFIRMADO en la documentación: el header exacto de autenticación de
///    GET /v1/transactions/{id}. La doc muestra la URL sin explicitarlo. Se intenta
///    primero con la llave PRIVADA y, ante 401/403, se reintenta con la PÚBLICA
///    (varios ejemplos de la comunidad usan la pública para consultar). Hay que
///    verificarlo en sandbox; ambas rutas están cubiertas y el fallo no bloquea la venta.
/// </summary>
public sealed class WompiPaymentGateway : IPaymentGateway
{
    public const string SourceApi = "Api";

    private readonly HttpClient _http;
    private readonly WompiOptions _options;
    private readonly ILogger<WompiPaymentGateway> _logger;

    public WompiPaymentGateway(
        HttpClient http,
        IOptions<WompiOptions> options,
        ILogger<WompiPaymentGateway> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool CanCharge => _options.CanCharge;

    // ------------------------------------------------------------- Referencia

    /// <summary>
    /// <c>BEAN-{OrderID:N}-{intento}</c>. Alfanumérica con guiones (formato admitido por
    /// Wompi) y, sobre todo, REVERSIBLE: un webhook tardío de un intento anterior sigue
    /// siendo resoluble hasta su pedido sin necesidad de una tabla de intentos.
    /// </summary>
    public string BuildReference(Guid orderId, int attempt)
    {
        var prefix = string.IsNullOrWhiteSpace(_options.ReferencePrefix) ? "BEAN" : _options.ReferencePrefix.Trim();
        if (attempt < 1) attempt = 1;

        return string.Create(CultureInfo.InvariantCulture, $"{prefix}-{orderId:N}-{attempt}");
    }

    public bool TryParseReference(string? reference, out Guid orderId, out int attempt)
    {
        orderId = Guid.Empty;
        attempt = 0;

        if (string.IsNullOrWhiteSpace(reference)) return false;

        // Se leen los DOS últimos segmentos, no los del prefijo: así un ReferencePrefix
        // con guiones no rompe la resolución.
        var parts = reference.Trim().Split('-');
        if (parts.Length < 3) return false;

        if (!Guid.TryParseExact(parts[^2], "N", out orderId)) return false;

        return int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out attempt);
    }

    // --------------------------------------------------------- Web Checkout

    public PaymentCheckoutRequest BuildCheckout(Order order, string redirectUrl, string? customerEmail)
    {
        ArgumentNullException.ThrowIfNull(order);

        var reference = order.PaymentReference
            ?? throw new InvalidOperationException("El pedido no tiene referencia de pago asignada.");

        var amountInCents = order.PaymentAmountInCents
            ?? PaymentAmounts.ToCents(order.TotalAmount);

        var currency = _options.CurrencyOrDefault;

        // expiration-time: si se envía, DEBE entrar también en la cadena de la firma.
        var expirationIso = ToIso8601Utc(order.PaymentExpiresAt);

        var signature = WompiSignature.Integrity(
            reference,
            amountInCents,
            currency,
            _options.IntegritySecret,
            expirationIso);

        var fullName = $"{order.FirstName} {order.LastName}".Trim();

        return new PaymentCheckoutRequest(
            CheckoutUrl: string.IsNullOrWhiteSpace(_options.CheckoutUrl)
                ? "https://checkout.wompi.co/p/"
                : _options.CheckoutUrl.Trim(),
            PublicKey: _options.PublicKey,
            Currency: currency,
            AmountInCents: amountInCents,
            Reference: reference,
            IntegritySignature: signature,
            RedirectUrl: redirectUrl,
            ExpirationTimeIso: expirationIso,
            CustomerEmail: customerEmail,
            CustomerFullName: string.IsNullOrWhiteSpace(fullName) ? null : fullName,
            ShippingAddressLine1: order.Address,
            ShippingCity: order.ShippingCity,
            ShippingRegion: order.State,
            ShippingCountry: ToIsoCountry(order.ShippingCountry),
            OrderID: order.OrderID,
            AmountInPesos: order.TotalAmount);
    }

    /// <summary>Formato exigido por Wompi: ISO-8601 en UTC con milisegundos ("…T20:28:50.000Z").</summary>
    internal static string? ToIso8601Utc(DateTime? value)
    {
        if (value is null) return null;

        var utc = value.Value.Kind == DateTimeKind.Utc
            ? value.Value
            : DateTime.SpecifyKind(value.Value, DateTimeKind.Local).ToUniversalTime();

        return utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>Wompi pide el país en ISO 3166-1 alpha-2. El formulario guarda "Colombia".</summary>
    internal static string? ToIsoCountry(string? country)
    {
        if (string.IsNullOrWhiteSpace(country)) return null;

        var value = country.Trim();
        if (value.Length == 2) return value.ToUpperInvariant();

        return value.Equals("Colombia", StringComparison.OrdinalIgnoreCase) ? "CO" : null;
    }

    // ------------------------------------------------- Consulta al API (respaldo)

    public async Task<PaymentSnapshot?> GetTransactionAsync(string transactionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(transactionId)) return null;
        if (!_options.CanQueryApi)
        {
            _logger.LogWarning("No hay llaves de Wompi cargadas: no se puede consultar el estado de la transacción.");
            return null;
        }

        var url = $"{BaseUrl()}/transactions/{Uri.EscapeDataString(transactionId.Trim())}";

        // 1º con la llave privada; si el API la rechaza, se reintenta con la pública
        // (el header exacto no está documentado; ver nota de la clase).
        var snapshot = await TryGetAsync(url, _options.PrivateKey, ct);
        if (snapshot is not null) return snapshot;

        if (!string.Equals(_options.PrivateKey, _options.PublicKey, StringComparison.Ordinal)
            && WompiOptions.HasValue(_options.PublicKey))
        {
            return await TryGetAsync(url, _options.PublicKey, ct);
        }

        return null;
    }

    private async Task<PaymentSnapshot?> TryGetAsync(string url, string? bearer, CancellationToken ct)
    {
        if (!WompiOptions.HasValue(bearer)) return null;

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSecondsOrDefault));

            using var message = new HttpRequestMessage(HttpMethod.Get, url);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            message.Headers.Accept.ParseAdd("application/json");

            using var response = await _http.SendAsync(message, timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                // 401/403 => probablemente la llave equivocada: se reintenta con la otra.
                _logger.LogWarning(
                    "Wompi respondió HTTP {StatusCode} al consultar la transacción.",
                    (int)response.StatusCode);
                return null;
            }

            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object)
            {
                _logger.LogWarning("Respuesta inesperada de Wompi al consultar la transacción (sin objeto 'data').");
                return null;
            }

            return FromTransactionJson(data, _options.EventEnvironment, SourceApi);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Timeout consultando el estado de la transacción en Wompi.");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Error de red consultando el estado de la transacción en Wompi.");
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "No se pudo interpretar la respuesta de Wompi al consultar la transacción.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error inesperado consultando el estado de la transacción en Wompi.");
            return null;
        }
    }

    private string BaseUrl() =>
        (string.IsNullOrWhiteSpace(_options.BaseUrl) ? "https://sandbox.wompi.co/v1" : _options.BaseUrl.Trim())
        .TrimEnd('/');

    // ------------------------------------------------------------- Mapeo JSON

    /// <summary>
    /// Construye la foto del pago a partir del objeto <c>transaction</c> de Wompi.
    /// Lo comparten la consulta al API y el webhook (mismo formato de objeto).
    /// </summary>
    public static PaymentSnapshot? FromTransactionJson(JsonElement transaction, string? environment, string source)
    {
        if (transaction.ValueKind != JsonValueKind.Object) return null;

        var reference = ReadString(transaction, "reference");
        if (string.IsNullOrWhiteSpace(reference)) return null;

        long? amountInCents = null;
        if (transaction.TryGetProperty("amount_in_cents", out var amount)
            && amount.ValueKind == JsonValueKind.Number
            && amount.TryGetInt64(out var cents))
        {
            amountInCents = cents;
        }

        return new PaymentSnapshot(
            Reference: reference!,
            TransactionId: ReadString(transaction, "id"),
            Status: MapStatus(ReadString(transaction, "status")),
            AmountInCents: amountInCents,
            PaymentMethodType: Truncate(ReadString(transaction, "payment_method_type"), 40),
            StatusMessage: Truncate(ReadString(transaction, "status_message"), 255),
            Environment: environment,
            Currency: ReadString(transaction, "currency"),
            Source: source);
    }

    /// <summary>
    /// Estados de Wompi: PENDING (inicial) y los finales APPROVED, DECLINED, VOIDED y ERROR.
    /// Lo desconocido se trata como PENDING: no se toca nada y el webhook siguiente resuelve.
    /// </summary>
    public static PaymentStatus MapStatus(string? status) => status?.Trim().ToUpperInvariant() switch
    {
        "APPROVED" => PaymentStatus.Approved,
        "DECLINED" => PaymentStatus.Declined,
        "VOIDED" => PaymentStatus.Voided,
        "ERROR" => PaymentStatus.Error,
        _ => PaymentStatus.Pending
    };

    private static string? ReadString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var text = value.Trim();
        return text.Length <= max ? text : text.Substring(0, max);
    }
}

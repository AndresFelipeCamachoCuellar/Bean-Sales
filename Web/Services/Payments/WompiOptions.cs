using System.Diagnostics.CodeAnalysis;

namespace Web.Services.Payments;

/// <summary>
/// Configuración de la pasarela (sección "Wompi" de appsettings). Mismo patrón que
/// <see cref="Web.Services.Shipping.ShippingOptions"/>: los SECRETOS nunca viven en
/// appsettings.json commiteado — van en appsettings.Development.json (gitignored) o
/// inyectados por el pipeline desde GitHub Secrets (Wompi__PrivateKey, etc.).
///
/// Llaves de Wompi (4 por ambiente):
///   · PublicKey       (pub_test_ / pub_prod_)  — ÚNICA que puede llegar al navegador.
///   · PrivateKey      (prv_test_ / prv_prod_)  — solo backend (consultar transacciones).
///   · IntegritySecret (test_integrity_ / prod_integrity_) — firmar la transacción.
///   · EventsSecret    (test_events_ / prod_events_)       — validar el webhook.
/// </summary>
public class WompiOptions
{
    /// <summary>
    /// Interruptor general. En false (o sin llaves) el checkout mantiene EXACTAMENTE el
    /// comportamiento anterior: pedido confirmado sin cobrar. Permite mergear a `qa`
    /// mientras Andrés completa el onboarding de Wompi.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>"Sandbox" | "Production". Solo se usa para derivar el ambiente y avisar
    /// si las llaves no corresponden.</summary>
    public string Environment { get; set; } = "Sandbox";

    /// <summary>URL base del API. Sandbox: https://sandbox.wompi.co/v1 · Producción: https://production.wompi.co/v1</summary>
    public string BaseUrl { get; set; } = "https://sandbox.wompi.co/v1";

    /// <summary>Web Checkout (redirección). Es igual en ambos ambientes: el ambiente lo define la llave.</summary>
    public string CheckoutUrl { get; set; } = "https://checkout.wompi.co/p/";

    /// <summary>Llave pública. Es la única que puede viajar al navegador.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>SECRETO. Solo backend: consultar transacciones.</summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>SECRETO. Firma de integridad de la transacción. Jamás al frontend ni a logs.</summary>
    public string IntegritySecret { get; set; } = string.Empty;

    /// <summary>SECRETO. Valida el checksum de los eventos (webhook). Jamás al frontend ni a logs.</summary>
    public string EventsSecret { get; set; } = string.Empty;

    /// <summary>Única moneda disponible en Wompi hoy.</summary>
    public string Currency { get; set; } = "COP";

    /// <summary>
    /// Vigencia de la reserva de stock y del <c>expiration-time</c> enviado a Wompi.
    /// 45 min: PSE obliga a salir al portal del banco, autenticarse y aprobar.
    /// </summary>
    public int PaymentExpirationMinutes { get; set; } = 45;

    /// <summary>Timeout duro de la consulta de estado. Nunca debe bloquear al cliente.</summary>
    public int TimeoutSeconds { get; set; } = 8;

    /// <summary>Prefijo de la referencia de pago (<c>BEAN-{orderId:N}-{intento}</c>).</summary>
    public string ReferencePrefix { get; set; } = "BEAN";

    // ------------------------------------------------------------------ Derivados

    /// <summary>"test" | "prod": el valor que Wompi manda en <c>environment</c> del evento.</summary>
    public string EventEnvironment =>
        Environment?.Trim().StartsWith("prod", StringComparison.OrdinalIgnoreCase) == true ? "prod" : "test";

    /// <summary>
    /// ¿Se puede iniciar un cobro? Hace falta la llave pública (identifica al comercio)
    /// y el secreto de integridad (firma el monto). Sin ellos, Wompi rechaza la transacción.
    /// </summary>
    public bool CanCharge => Enabled && HasValue(PublicKey) && HasValue(IntegritySecret);

    /// <summary>¿Se pueden validar eventos entrantes? Sin este secreto el webhook no autentica nada.</summary>
    public bool CanValidateEvents => HasValue(EventsSecret);

    /// <summary>¿Se puede consultar el estado de una transacción por API?</summary>
    public bool CanQueryApi => HasValue(PrivateKey) || HasValue(PublicKey);

    public int ExpirationMinutesOrDefault => PaymentExpirationMinutes > 0 ? PaymentExpirationMinutes : 45;

    public int TimeoutSecondsOrDefault => TimeoutSeconds > 0 ? TimeoutSeconds : 8;

    public string CurrencyOrDefault => string.IsNullOrWhiteSpace(Currency) ? "COP" : Currency.Trim().ToUpperInvariant();

    /// <summary>
    /// Un valor cuenta como cargado si no está vacío y no es uno de los marcadores
    /// "PEGA_AQUI_…" que dejamos en la plantilla de configuración local.
    /// </summary>
    public static bool HasValue([NotNullWhen(true)] string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.TrimStart().StartsWith("PEGA_AQUI", StringComparison.OrdinalIgnoreCase);
}

namespace Web.Services.Shipping;

/// <summary>
/// Configuración de envío (sección "Shipping" de appsettings). Los secretos
/// (API keys) NUNCA viven en appsettings.json: van en appsettings.Development.json
/// (gitignored) o en variables de entorno del host (Shipping__...).
/// </summary>
public class ShippingOptions
{
    /// <summary>"Fixed" | "Mipaquete". Se lee una sola vez, en el arranque.</summary>
    public string Provider { get; set; } = "Fixed";

    /// <summary>Tarifa de respaldo en COP. Es la tarifa única mientras Provider = "Fixed".</summary>
    public decimal FallbackCost { get; set; } = 18000m;

    /// <summary>Días hábiles estimados que se prometen con la tarifa de respaldo.</summary>
    public int FallbackDays { get; set; } = 4;

    /// <summary>TTL de la caché de cotizaciones, en minutos.</summary>
    public int QuoteCacheMinutes { get; set; } = 15;

    /// <summary>TTL de la caché del catálogo de ciudades, en horas.</summary>
    public int CityCacheHours { get; set; } = 12;

    public ShippingOriginOptions Origin { get; set; } = new();

    public ShippingDefaultsOptions Defaults { get; set; } = new();

    public MipaqueteOptions Mipaquete { get; set; } = new();
}

/// <summary>Bodega de origen. Dato bloqueante para cotizar de verdad (pendiente del PO).</summary>
public class ShippingOriginOptions
{
    public string DaneCode { get; set; } = string.Empty;
    public string CityName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
}

/// <summary>
/// Valores por defecto y parámetros de la heurística de empaque.
/// Se aplican cuando el producto no tiene peso/dimensiones cargados.
/// </summary>
public class ShippingDefaultsOptions
{
    /// <summary>Peso asumido por unidad cuando el producto no lo tiene (bolsa de 500 g con válvula).</summary>
    public int WeightGrams { get; set; } = 500;

    public decimal LengthCm { get; set; } = 20m;
    public decimal WidthCm { get; set; } = 12m;
    public decimal HeightCm { get; set; } = 8m;

    /// <summary>Holgura de caja/relleno aplicada al volumen agregado.</summary>
    public decimal PackingFactor { get; set; } = 1.15m;

    public decimal MinBoxLengthCm { get; set; } = 15m;
    public decimal MinBoxWidthCm { get; set; } = 15m;
    public decimal MinBoxHeightCm { get; set; } = 10m;

    /// <summary>Peso máximo por envío (kg). Por encima se bloquea y se deriva a contacto comercial.</summary>
    public decimal MaxPackageWeightKg { get; set; } = 30m;

    /// <summary>Peso mínimo facturable por las transportadoras nacionales (kg).</summary>
    public decimal MinBillableWeightKg { get; set; } = 1m;
}

/// <summary>
/// Integración con la API de mipaquete.com.
///
/// ⚠️ AUTENTICACIÓN (contrato REAL, validado en vivo jul-2026 contra
/// POST https://core.mipaquete.com/routes/quoteShipping):
/// el endpoint de COTIZACIÓN se autentica con los headers
/// <c>customer-key</c> (el UUID del comercio, ver <see cref="CustomerKey"/>) y
/// <c>session-tracker</c>. NO se envía el header <c>apikey</c>.
///
/// La documentación pública describía otro esquema ("apikey" con el JWT de la
/// cuenta) que corresponde al entorno DEV y NO funciona contra core.mipaquete.com.
/// <see cref="ApiKey"/> se conserva por compatibilidad y para los endpoints que sí
/// la exigen (generación de guía/tracking, pendientes de v1.1).
/// </summary>
public class MipaqueteOptions
{
    /// <summary>
    /// Host de la API. Por defecto, el confirmado en vivo (core.mipaquete.com).
    /// Es configurable para poder apuntar al entorno de pruebas
    /// ("https://api-v2.dev.mpr.mipaquete.com") desde el panel del host sin recompilar.
    /// </summary>
    public string BaseUrl { get; set; } = "https://core.mipaquete.com";

    /// <summary>
    /// Ruta del endpoint de cotización, relativa a <see cref="BaseUrl"/>.
    /// "routes/quoteShipping" está CONFIRMADO en vivo contra core.mipaquete.com;
    /// se deja como configuración por si la API versiona la ruta, para corregirla
    /// desde el panel del host sin recompilar (un 404 degrada a la tarifa de respaldo,
    /// no rompe la venta).
    /// </summary>
    public string QuotePath { get; set; } = "routes/quoteShipping";

    /// <summary>Timeout duro por intento. Por encima de ~5 s el usuario abandona el checkout.</summary>
    public int TimeoutSeconds { get; set; } = 4;

    /// <summary>
    /// Identificador del comercio (UUID) que viaja en el header <c>customer-key</c>.
    /// Es LA credencial que exige /routes/quoteShipping. Sin este valor no se llama a
    /// la API: se degrada a la tarifa de respaldo.
    /// No es un secreto criptográfico, pero identifica la cuenta ⇒ se trata como tal:
    /// nunca en appsettings.json commiteado ni en logs (va en
    /// appsettings.Development.json y en el secret MIPAQUETE_CUSTOMER_KEY).
    /// </summary>
    public string CustomerKey { get; set; } = string.Empty;

    /// <summary>
    /// Código de país del ORIGEN en el payload de cotización. "170" = Colombia
    /// (confirmado en el tráfico real del panel de mipaquete).
    /// </summary>
    public string OriginCountryCode { get; set; } = ColombiaCountryCode;

    /// <summary>Código de país del DESTINO. "170" = Colombia (todo el MVP es nacional).</summary>
    public string DestinyCountryCode { get; set; } = ColombiaCountryCode;

    /// <summary>Código de país de Colombia tal como lo espera mipaquete en el payload.</summary>
    public const string ColombiaCountryCode = "170";

    /// <summary>
    /// JWT de la cuenta. NO se usa al cotizar (ver la nota de la clase); queda
    /// disponible para los endpoints que sí lo exigen. SECRETO: nunca en
    /// appsettings.json commiteado ni en logs.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// GUID del header "session-tracker" que exige la API. Es OPCIONAL en configuración:
    /// si se deja vacío, el servicio genera un GUID estable al arrancar y lo usa durante
    /// toda la vida del proceso. Fijarlo aquí solo sirve para correlacionar llamadas
    /// entre reinicios. NO es un secreto, pero tampoco se registra en logs.
    /// </summary>
    public string SessionTracker { get; set; } = string.Empty;
}

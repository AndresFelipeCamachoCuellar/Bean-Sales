using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Web.Services.Shipping;

/// <summary>
/// Cotizador real contra la API de mipaquete.com.
///
/// Reglas no negociables (ADR-014 §3.4):
///  · NUNCA lanza por fallo de la API: cualquier error de red/HTTP degrada a la
///    tarifa fija (<see cref="ShippingQuoteStatus.Fallback"/>) y la venta continúa.
///  · Solo "sin cobertura" bloquea (<see cref="ShippingQuoteStatus.NoCoverage"/>):
///    ahí el problema no es cotizar, es que físicamente no se puede entregar.
///  · Las credenciales jamás se escriben en logs.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// CONTRATO REAL, VALIDADO EN VIVO (jul-2026) — no es el de la doc pública
/// ─────────────────────────────────────────────────────────────────────────────
/// La documentación pública describe el entorno DEV (api-v2.dev.mpr.mipaquete.com)
/// con el header "apikey"; ese esquema NO funciona contra producción. El contrato
/// de abajo se obtuvo inspeccionando el tráfico del panel de mipaquete y se probó
/// con éxito (devolvió 4 transportadoras).
///
/// Autenticación: DOS headers de texto plano,
///   customer-key:    UUID del comercio  (Shipping:Mipaquete:CustomerKey)
///   session-tracker: un GUID            (ver <see cref="SessionTracker"/>)
/// ⚠️ NO se envía el header "apikey" en este endpoint.
///
/// POST {BaseUrl}/routes/quoteShipping
///   {
///     "originCountryCode": "170",         // 170 = Colombia
///     "originLocationCode": "76001000",   // DANE(5) + sufijo de zona(3)
///     "destinyCountryCode": "170",
///     "destinyLocationCode": "11001000",  // ⚠️ "destiny", no "destination"
///     "height": 10, "width": 10, "length": 10,   // cm, ENTEROS
///     "weight": 3,                                // kg, ENTERO
///     "quantity": 1,
///     "declaredValue": 10000,
///     "saleValue": 0                      // 0 = sin pago contraentrega
///   }
///   · "length" es el nombre aquí; en createSending el mismo dato se llama "large".
///
/// 200 OK ⇒ ARRAY de opciones:
///   [{ "id", "deliveryCompanyName": "SERVIENTREGA", "shippingCost": 20650,
///      "shippingTime": 2880,           // ⚠️ MINUTOS (2880 = 2 días)
///      "deliveryCompanyId": "5cb0...", "deliveryCompanyImgUrl": "https://...",
///      "score": 4, "routeType": "nacional", "type": "messaging", ... }]
///   · Array VACÍO ⇒ no hay transportadora para esa ruta ⇒ NoCoverage.
///
/// Entorno de pruebas: apuntar Shipping:Mipaquete:BaseUrl a
/// "https://api-v2.dev.mpr.mipaquete.com" (el default queda en producción).
/// </summary>
public sealed class MipaqueteShippingQuoteService : IShippingQuoteService
{
    public const string SourceName = "Mipaquete";
    public const string FallbackSourceName = "Fallback";

    private const string FallbackMessage =
        "No pudimos cotizar con la transportadora; se aplica una tarifa estándar.";

    private const string NoCoverageMessage =
        "Todavía no tenemos cobertura para ese destino. Escríbenos y lo gestionamos contigo.";

    private const int MaxAttempts = 2;      // 1 intento + 1 reintento
    private const int RetryDelayMs = 500;
    private const decimal PriceRoundingStep = 50m;  // en COP no existen monedas menores
    private const int MinutesPerDay = 1440;         // shippingTime viene en minutos
    private const int MaxEstimatedDays = 60;        // techo sanitario para un dato absurdo

    // JsonSerializerDefaults.Web ⇒ camelCase al escribir, case-insensitive al leer y
    // NumberHandling.AllowReadingFromString (tolera "22200" además de 22200).
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Fallback del session-tracker: un GUID estable por instancia de la app. La API
    // solo exige que sea un GUID; usar el mismo durante toda la vida del proceso
    // permite que mipaquete correlacione nuestras llamadas.
    private static readonly string InstanceSessionTracker = Guid.NewGuid().ToString();

    // Único resto de heurística, y a propósito: si un 4xx trae un mensaje de falta de
    // cobertura, es mejor bloquear que prometer una entrega imposible por 18.000 COP.
    private static readonly string[] NoCoverageHints =
        { "cobertura", "covertura", "no hay servicio", "sin servicio", "no existe ruta" };

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ShippingOptions _options;
    private readonly FixedShippingQuoteService _fallback;
    private readonly ILogger<MipaqueteShippingQuoteService> _logger;

    public MipaqueteShippingQuoteService(
        HttpClient http,
        IMemoryCache cache,
        IOptions<ShippingOptions> options,
        FixedShippingQuoteService fallback,
        ILogger<MipaqueteShippingQuoteService> logger)
    {
        _http = http;
        _cache = cache;
        _options = options.Value;
        _fallback = fallback;
        _logger = logger;
    }

    public async Task<ShippingQuoteResult> QuoteAsync(ShippingQuoteRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ---------- 1. Input del usuario ----------
        if (string.IsNullOrWhiteSpace(request.DestinationDaneCode))
        {
            return Invalid("Elige tu ciudad para calcular el costo del envío.");
        }

        if (request.WeightKg <= 0m)
        {
            return Invalid("No pudimos calcular el peso del pedido. Revisa las cantidades del carrito.");
        }

        // ---------- 2. Configuración ----------
        // Un problema de configuración NO puede costarle la venta al cliente: se degrada.
        var mipaquete = _options.Mipaquete;

        var originDane = !string.IsNullOrWhiteSpace(request.OriginDaneCode)
            ? request.OriginDaneCode
            : _options.Origin.DaneCode;

        if (string.IsNullOrWhiteSpace(originDane))
        {
            _logger.LogWarning(
                "Shipping:Origin:DaneCode está vacío: falta el código DANE de la bodega de origen. " +
                "Se aplica la tarifa de respaldo.");
            return BuildFallback();
        }

        // La credencial del endpoint de cotización es customer-key (NO la ApiKey/JWT):
        // sin ella la llamada sería un 401 seguro, así que ni se intenta.
        if (string.IsNullOrWhiteSpace(mipaquete.CustomerKey))
        {
            _logger.LogWarning(
                "Shipping:Mipaquete:CustomerKey no está configurada (Provider = Mipaquete). " +
                "Se aplica la tarifa de respaldo hasta que se cargue la credencial en el host.");
            return BuildFallback();
        }

        var origin = ToLocationCode(originDane);
        var destination = ToLocationCode(request.DestinationDaneCode);

        if (origin.Length == 0 || destination.Length == 0)
        {
            _logger.LogWarning(
                "Códigos de ubicación inutilizables (origen '{Origin}', destino '{Destination}'). " +
                "Se aplica la tarifa de respaldo.",
                originDane, request.DestinationDaneCode);
            return BuildFallback();
        }

        // ---------- 3. Payload ----------
        // La API espera enteros: se redondea SIEMPRE hacia arriba (así cobra la
        // transportadora). El mínimo facturable de 1 kg ya lo aplicó ShippingPackageBuilder
        // (Defaults:MinBillableWeightKg); el piso de 1 de ToPositiveInt solo protege de un
        // request armado a mano y de que la API rechace un 0.
        var payload = new MipaqueteQuoteRequestDto
        {
            OriginCountryCode = CountryCodeOrDefault(mipaquete.OriginCountryCode),
            OriginLocationCode = origin,
            DestinyCountryCode = CountryCodeOrDefault(mipaquete.DestinyCountryCode),
            DestinyLocationCode = destination,
            Weight = ToPositiveInt(request.WeightKg),
            Length = ToPositiveInt(request.LengthCm),
            Width = ToPositiveInt(request.WidthCm),
            Height = ToPositiveInt(request.HeightCm),
            Quantity = 1,
            DeclaredValue = ToNonNegativeLong(request.DeclaredValue),
            // 0 = sin pago contraentrega. El campo queda cableado para cuando se habilite.
            SaleValue = 0L
        };

        // ---------- 4. Caché ----------
        // La clave se arma con el payload REAL (ya redondeado a enteros): dos carritos
        // que generan el mismo envío comparten cotización y el hit rate sube.
        var cacheKey = BuildCacheKey(payload);
        if (_cache.TryGetValue(cacheKey, out ShippingQuoteResult? cached) && cached is not null)
        {
            return cached;
        }

        // ---------- 5. Llamada HTTP (timeout + 1 reintento) ----------
        string body;
        try
        {
            body = JsonSerializer.Serialize(payload, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo serializar la solicitud de cotización de envío.");
            return BuildFallback();
        }

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var result = await TryQuoteOnceAsync(body, destination, attempt, ct);

            if (result is not null)
            {
                // Solo se cachea una cotización REAL: cachear un fallo lo perpetuaría.
                if (result.Status == ShippingQuoteStatus.Ok)
                {
                    _cache.Set(cacheKey, result, TimeSpan.FromMinutes(QuoteCacheMinutes));
                }
                return result;
            }

            if (attempt >= MaxAttempts || ct.IsCancellationRequested) break;

            try
            {
                await Task.Delay(RetryDelayMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return BuildFallback();
    }

    /// <summary>
    /// Un intento contra la API. Devuelve el resultado definitivo, o <c>null</c> si
    /// vale la pena reintentar (timeout o 5xx).
    /// </summary>
    private async Task<ShippingQuoteResult?> TryQuoteOnceAsync(
        string jsonBody,
        string destination,
        int attempt,
        CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

            using var message = new HttpRequestMessage(HttpMethod.Post, BuildQuoteUrl());
            // Autenticación: dos headers de texto plano (contrato REAL, validado en vivo).
            // ⚠️ Aquí NO va el header "apikey": el endpoint de cotización usa "customer-key".
            // TryAddWithoutValidation evita que un valor con caracteres raros tire una excepción.
            message.Headers.TryAddWithoutValidation("customer-key", _options.Mipaquete.CustomerKey.Trim());
            message.Headers.TryAddWithoutValidation("session-tracker", SessionTracker);
            message.Headers.Accept.ParseAdd("application/json");
            message.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(message, timeoutCts.Token);
            var responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            if (response.IsSuccessStatusCode)
            {
                if (!TryParseQuotes(responseBody, out var quotes))
                {
                    _logger.LogWarning(
                        "No se pudo interpretar la respuesta de cotización de mipaquete para el destino {Destination} " +
                        "(se esperaba un array de opciones). Se aplica la tarifa de respaldo.",
                        destination);
                    return BuildFallback();
                }

                // Contrato confirmado: array vacío = no hay transportadora para esa ruta.
                if (quotes.Count == 0)
                {
                    _logger.LogInformation(
                        "mipaquete no devolvió transportadoras para el destino {Destination}: se marca sin cobertura.",
                        destination);
                    return NoCoverage();
                }

                var options = quotes
                    .Select(MapOption)
                    .Where(o => o is not null)
                    .Select(o => o!)
                    .OrderBy(o => o.Price)
                    .ThenBy(o => o.EstimatedDays)
                    .ToList();

                if (options.Count == 0)
                {
                    // Hubo opciones pero ninguna utilizable (p. ej. shippingCost en 0):
                    // no es falta de cobertura, es un dato inesperado ⇒ no se bloquea la venta.
                    _logger.LogWarning(
                        "mipaquete devolvió {Count} opciones sin costo válido para {Destination}. " +
                        "Se aplica la tarifa de respaldo.",
                        quotes.Count, destination);
                    return BuildFallback();
                }

                return new ShippingQuoteResult
                {
                    Status = ShippingQuoteStatus.Ok,
                    Options = options,
                    Source = SourceName
                };
            }

            var statusCode = (int)response.StatusCode;

            if (statusCode is 401 or 403)
            {
                // Falla de configuración, no del cliente. Nivel Error para que se vea en el host.
                _logger.LogError(
                    "mipaquete rechazó las credenciales (HTTP {StatusCode}). Verifica Shipping:Mipaquete:CustomerKey " +
                    "(el UUID del comercio, NO el JWT) y que el perfil de la cuenta esté completo; sin eso la " +
                    "integración no responde.",
                    statusCode);
                return BuildFallback();
            }

            if (statusCode is >= 400 and < 500)
            {
                if (LooksLikeNoCoverage(responseBody))
                {
                    _logger.LogInformation(
                        "mipaquete reportó falta de cobertura para {Destination} (HTTP {StatusCode}).",
                        destination, statusCode);
                    return NoCoverage();
                }

                _logger.LogWarning(
                    "mipaquete respondió HTTP {StatusCode} al cotizar {Destination}. Se aplica la tarifa de respaldo.",
                    statusCode, destination);
                return BuildFallback();   // los 4xx no se arreglan reintentando
            }

            _logger.LogWarning(
                "mipaquete respondió HTTP {StatusCode} al cotizar {Destination} (intento {Attempt} de {MaxAttempts}).",
                statusCode, destination, attempt, MaxAttempts);
            return null;   // 5xx: vale la pena reintentar
        }
        catch (OperationCanceledException)
        {
            if (ct.IsCancellationRequested)
            {
                // El cliente abortó la petición: no hay a quién responder, no se reintenta.
                return BuildFallback();
            }

            _logger.LogWarning(
                "Timeout ({TimeoutSeconds}s) cotizando {Destination} con mipaquete (intento {Attempt} de {MaxAttempts}).",
                TimeoutSeconds, destination, attempt, MaxAttempts);
            return null;   // timeout: se reintenta una vez
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "Error de red cotizando {Destination} con mipaquete (intento {Attempt} de {MaxAttempts}).",
                destination, attempt, MaxAttempts);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Error inesperado cotizando {Destination} con mipaquete. Se aplica la tarifa de respaldo.",
                destination);
            return BuildFallback();
        }
    }

    // ---------------------------------------------------------------------
    // Resultados
    // ---------------------------------------------------------------------

    /// <summary>Reutiliza la tarifa plana como red de seguridad, marcada como "Fallback".</summary>
    private ShippingQuoteResult BuildFallback() =>
        _fallback.Build(FallbackSourceName, ShippingQuoteStatus.Fallback, FallbackMessage);

    private static ShippingQuoteResult NoCoverage() => new()
    {
        Status = ShippingQuoteStatus.NoCoverage,
        Options = Array.Empty<ShippingOption>(),
        Source = SourceName,
        Message = NoCoverageMessage
    };

    private static ShippingQuoteResult Invalid(string message) => new()
    {
        Status = ShippingQuoteStatus.Invalid,
        Options = Array.Empty<ShippingOption>(),
        Source = SourceName,
        Message = message
    };

    // ---------------------------------------------------------------------
    // Configuración derivada
    // ---------------------------------------------------------------------

    private int TimeoutSeconds =>
        _options.Mipaquete.TimeoutSeconds > 0 ? _options.Mipaquete.TimeoutSeconds : 4;

    private int QuoteCacheMinutes =>
        _options.QuoteCacheMinutes > 0 ? _options.QuoteCacheMinutes : 15;

    /// <summary>
    /// GUID que identifica la sesión ante mipaquete. Si no está configurado
    /// (Shipping:Mipaquete:SessionTracker), se usa uno generado al arrancar y estable
    /// durante toda la vida del proceso.
    /// </summary>
    private string SessionTracker =>
        string.IsNullOrWhiteSpace(_options.Mipaquete.SessionTracker)
            ? InstanceSessionTracker
            : _options.Mipaquete.SessionTracker.Trim();

    /// <summary>
    /// URL absoluta del endpoint de cotización. Se arma a mano (y no vía BaseAddress)
    /// para que un BaseUrl sin barra final no rompa la resolución relativa.
    /// </summary>
    private string BuildQuoteUrl()
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.Mipaquete.BaseUrl)
            ? "https://api-v2.mipaquete.com"
            : _options.Mipaquete.BaseUrl.Trim();

        var path = string.IsNullOrWhiteSpace(_options.Mipaquete.QuotePath)
            ? "quoteShipping"
            : _options.Mipaquete.QuotePath.Trim();

        return $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
    }

    // ---------------------------------------------------------------------
    // Mapeo de códigos de ciudad
    // ---------------------------------------------------------------------

    /// <summary>
    /// Traduce un código DANE del catálogo local (DIVIPOLA, 5 dígitos: "11001", "05045")
    /// al "locationCode" de mipaquete, que son esos 5 dígitos + un sufijo de zona de 3
    /// dígitos ("11001000"). Confirmado contra la respuesta real de getLocations.
    /// </summary>
    internal static string ToLocationCode(string? daneCode)
    {
        if (string.IsNullOrWhiteSpace(daneCode)) return string.Empty;

        var digits = new string(daneCode.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return string.Empty;

        if (digits.Length >= 8) return digits.Substring(0, 8);
        if (digits.Length > 5) return digits.PadRight(8, '0');

        return digits.PadLeft(5, '0') + "000";
    }

    // ---------------------------------------------------------------------
    // Normalización numérica (la API solo acepta enteros)
    // ---------------------------------------------------------------------

    /// <summary>cm y kg: hacia arriba y nunca por debajo de 1 (la API rechaza ceros).</summary>
    internal static int ToPositiveInt(decimal value)
    {
        if (value <= 1m) return 1;

        var rounded = Math.Ceiling(value);
        return rounded > int.MaxValue ? int.MaxValue : (int)rounded;
    }

    /// <summary>
    /// Código de país del payload. Si la configuración viene vacía se usa Colombia ("170"):
    /// mandar una cadena vacía haría que la API rechace la cotización.
    /// </summary>
    internal static string CountryCodeOrDefault(string? value) =>
        string.IsNullOrWhiteSpace(value) ? MipaqueteOptions.ColombiaCountryCode : value.Trim();

    /// <summary>Valor declarado en COP, entero y nunca negativo.</summary>
    internal static long ToNonNegativeLong(decimal value)
    {
        if (value <= 0m) return 0L;

        var rounded = Math.Ceiling(value);
        return rounded > long.MaxValue ? long.MaxValue : (long)rounded;
    }

    // ---------------------------------------------------------------------
    // Caché
    // ---------------------------------------------------------------------

    private static string BuildCacheKey(MipaqueteQuoteRequestDto payload)
    {
        // El valor declarado se agrupa a bloques de 10.000 COP: mueve poco el precio
        // (solo el seguro) y sin agrupar el hit rate sería ~0 %.
        var declaredBucket = payload.DeclaredValue / 10_000L;
        var box = FormattableString.Invariant($"{payload.Length}x{payload.Width}x{payload.Height}");

        return FormattableString.Invariant(
            $"shipping:quote:{payload.OriginLocationCode}:{payload.DestinyLocationCode}:{payload.Weight}:{box}:{declaredBucket}");
    }

    // ---------------------------------------------------------------------
    // Lectura de la respuesta (nombres confirmados)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Deserializa el array de opciones. Devuelve <c>false</c> solo si el cuerpo no es
    /// un array JSON válido (contrato roto); una lista vacía es una respuesta legítima
    /// y significa "sin cobertura".
    /// </summary>
    private static bool TryParseQuotes(string? responseBody, out IReadOnlyList<MipaqueteQuoteResponseDto> quotes)
    {
        quotes = Array.Empty<MipaqueteQuoteResponseDto>();

        if (string.IsNullOrWhiteSpace(responseBody)) return false;

        try
        {
            var parsed = JsonSerializer.Deserialize<List<MipaqueteQuoteResponseDto>>(responseBody, JsonOptions);
            if (parsed is null) return false;

            quotes = parsed.Where(q => q is not null).ToList();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private ShippingOption? MapOption(MipaqueteQuoteResponseDto dto)
    {
        var price = dto.ShippingCost;
        if (price is null || price.Value <= 0m) return null;

        var rawName = string.IsNullOrWhiteSpace(dto.DeliveryCompanyName) ? null : dto.DeliveryCompanyName!.Trim();
        var externalId = string.IsNullOrWhiteSpace(dto.DeliveryCompanyId) ? null : dto.DeliveryCompanyId!.Trim();

        return new ShippingOption
        {
            CarrierCode = rawName is not null ? rawName.ToUpperInvariant() : (externalId ?? "CARRIER"),
            CarrierName = rawName is not null ? Prettify(rawName) : "Transportadora aliada",
            Price = RoundUpToStep(price.Value),
            EstimatedDays = ToBusinessDays(dto.ShippingTime),
            Rating = NormalizeScore(dto.Score),
            CarrierExternalId = externalId,
            LogoUrl = SafeLogoUrl(dto.DeliveryCompanyImgUrl)
        };
    }

    /// <summary>
    /// "shippingTime" viene en MINUTOS (2880 = 2 días). Se convierte a días redondeando
    /// hacia arriba, con piso de 1 día: prometer "hoy mismo" no es realista y un 0
    /// rompería la copia del checkout.
    /// </summary>
    internal static int ToBusinessDays(decimal? minutes, int fallbackDays)
    {
        if (minutes is null || minutes.Value <= 0m)
        {
            return fallbackDays > 0 ? fallbackDays : 1;
        }

        var days = Math.Ceiling((double)minutes.Value / MinutesPerDay);
        if (days < 1d) days = 1d;
        if (days > MaxEstimatedDays) days = MaxEstimatedDays;

        return (int)days;
    }

    private int ToBusinessDays(decimal? minutes) =>
        ToBusinessDays(minutes, _options.FallbackDays);

    /// <summary>
    /// Logo de la transportadora ("deliveryCompanyImgUrl"). Se acepta SOLO si es una URL
    /// absoluta https: cualquier otra cosa se descarta para no inyectar en la vista un
    /// src arbitrario venido de un tercero (ni romper la página por contenido mixto).
    /// </summary>
    internal static string? SafeLogoUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var text = value.Trim();

        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri.AbsoluteUri
            : null;
    }

    /// <summary>"score" es una calificación de 0 a 5; fuera de rango se descarta o se topa.</summary>
    private static decimal? NormalizeScore(decimal? score)
    {
        if (score is null || score.Value < 0m) return null;
        return score.Value > 5m ? 5m : score.Value;
    }

    /// <summary>Redondea hacia arriba al múltiplo de 50 COP: protege el margen y evita centavos.</summary>
    private static decimal RoundUpToStep(decimal value) =>
        value <= 0m ? 0m : Math.Ceiling(value / PriceRoundingStep) * PriceRoundingStep;

    /// <summary>La API devuelve los nombres en mayúscula sostenida ("COORDINADORA").</summary>
    private static string Prettify(string value)
    {
        var text = value.Trim();
        if (text.Length == 0) return text;

        if (text != text.ToUpperInvariant()) return text;

        return CultureInfo.GetCultureInfo("es-CO").TextInfo.ToTitleCase(text.ToLowerInvariant());
    }

    private static bool LooksLikeNoCoverage(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody)) return false;

        var lowered = responseBody.ToLowerInvariant();
        return NoCoverageHints.Any(hint => lowered.Contains(hint, StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------
    // DTO de SALIDA — nombres exactos del contrato de /quoteShipping
    // ---------------------------------------------------------------------
    private sealed class MipaqueteQuoteRequestDto
    {
        /// <summary>"170" = Colombia.</summary>
        [JsonPropertyName("originCountryCode")]
        public string OriginCountryCode { get; set; } = MipaqueteOptions.ColombiaCountryCode;

        [JsonPropertyName("originLocationCode")]
        public string OriginLocationCode { get; set; } = string.Empty;

        /// <summary>País de destino ("170" = Colombia). Ojo: "destiny", no "destination".</summary>
        [JsonPropertyName("destinyCountryCode")]
        public string DestinyCountryCode { get; set; } = MipaqueteOptions.ColombiaCountryCode;

        /// <summary>⚠️ El contrato dice "destiny", no "destination".</summary>
        [JsonPropertyName("destinyLocationCode")]
        public string DestinyLocationCode { get; set; } = string.Empty;

        /// <summary>Alto del bulto en cm (entero).</summary>
        [JsonPropertyName("height")]
        public int Height { get; set; }

        /// <summary>Ancho del bulto en cm (entero).</summary>
        [JsonPropertyName("width")]
        public int Width { get; set; }

        /// <summary>Largo del bulto en cm (entero). En createSending este campo se llama "large".</summary>
        [JsonPropertyName("length")]
        public int Length { get; set; }

        /// <summary>Peso en kg (entero).</summary>
        [JsonPropertyName("weight")]
        public int Weight { get; set; }

        /// <summary>Nº de bultos con esa misma medida. El MVP consolida todo en uno solo.</summary>
        [JsonPropertyName("quantity")]
        public int Quantity { get; set; } = 1;

        /// <summary>Valor a asegurar, en COP.</summary>
        [JsonPropertyName("declaredValue")]
        public long DeclaredValue { get; set; }

        /// <summary>
        /// Valor a recaudar en pago contraentrega. El panel real SIEMPRE lo envía, con 0
        /// cuando no hay recaudo — que es el caso del MVP. Queda cableado para cuando se
        /// habilite la contraentrega.
        /// </summary>
        [JsonPropertyName("saleValue")]
        public long SaleValue { get; set; }
    }

    // ---------------------------------------------------------------------
    // DTO de ENTRADA — solo los campos que usamos (el resto de la respuesta se ignora)
    // ---------------------------------------------------------------------
    private sealed class MipaqueteQuoteResponseDto
    {
        /// <summary>Id de ESTA cotización (no de la transportadora).</summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("deliveryCompanyName")]
        public string? DeliveryCompanyName { get; set; }

        /// <summary>Id de la transportadora: necesario para generar la guía más adelante.</summary>
        [JsonPropertyName("deliveryCompanyId")]
        public string? DeliveryCompanyId { get; set; }

        /// <summary>Logo de la transportadora (URL absoluta). Se muestra en el checkout.</summary>
        [JsonPropertyName("deliveryCompanyImgUrl")]
        public string? DeliveryCompanyImgUrl { get; set; }

        /// <summary>Costo del envío en COP.</summary>
        [JsonPropertyName("shippingCost")]
        public decimal? ShippingCost { get; set; }

        /// <summary>⚠️ Tiempo de entrega en MINUTOS (2880 = 2 días).</summary>
        [JsonPropertyName("shippingTime")]
        public decimal? ShippingTime { get; set; }

        /// <summary>Calificación del servicio, de 0 a 5.</summary>
        [JsonPropertyName("score")]
        public decimal? Score { get; set; }
    }
}

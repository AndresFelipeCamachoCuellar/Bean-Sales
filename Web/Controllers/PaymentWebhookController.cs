using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Web.Services.Payments;

namespace Web.Controllers;

/// <summary>
/// Webhook de Wompi: <c>POST /pagos/wompi/eventos</c>.
///
/// Es la ÚNICA fuente de verdad del pago (la propia documentación lo dice:
/// "no utilices la redirección como método de validación de tus transacciones").
///
/// El endpoint es anónimo pero NO está desprotegido: **la autenticación es la firma**.
/// Sin un checksum válido se responde 401 y no se escribe absolutamente nada en la BD.
///
/// Se usa routing por atributo para tener una URL corta, estable y ajena al nombre de
/// la clase: si el controlador se renombra, la URL registrada en el dashboard de Wompi
/// sigue funcionando.
/// </summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[Route("pagos/wompi")]
public class PaymentWebhookController : Controller
{
    /// <summary>Guardia barata contra abuso: un evento de Wompi son ~1 KB.</summary>
    private const int MaxBodyBytes = 64 * 1024;

    private const string ChecksumHeader = "X-Event-Checksum";
    private const string TransactionEvent = "transaction.updated";

    private readonly PaymentApplicationService _payments;
    private readonly WompiOptions _options;
    private readonly ILogger<PaymentWebhookController> _logger;

    public PaymentWebhookController(
        PaymentApplicationService payments,
        IOptions<WompiOptions> options,
        ILogger<PaymentWebhookController> logger)
    {
        _payments = payments;
        _options = options.Value;
        _logger = logger;
    }

    [HttpPost("eventos")]
    public async Task<IActionResult> Eventos(CancellationToken ct)
    {
        // Sin el secreto de eventos no se puede autenticar NADA: se responde 503 para
        // que Wompi reintente (3 veces en 24 h) una vez se cargue la credencial.
        if (!_options.CanValidateEvents)
        {
            _logger.LogError(
                "Llegó un evento de Wompi pero Wompi:EventsSecret no está configurado. " +
                "Carga el secreto de eventos en el host y reinicia la app.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (Request.ContentLength is > MaxBodyBytes)
        {
            _logger.LogWarning("Evento de Wompi descartado: cuerpo demasiado grande.");
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        // Cuerpo CRUDO: nada de model binding. La firma se calcula sobre los valores
        // exactos que envió Wompi.
        string body;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync(ct);
        }

        if (string.IsNullOrWhiteSpace(body) || body.Length > MaxBodyBytes)
        {
            return BadRequest();
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            _logger.LogWarning("Evento de Wompi descartado: el cuerpo no es JSON válido.");
            return BadRequest();
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return BadRequest();

            // ---------- 1. Validar la firma ----------
            if (!TryValidateChecksum(root, out var motivo))
            {
                _logger.LogWarning("Evento de Wompi RECHAZADO ({Motivo}). No se tocó la base de datos.", motivo);
                return Unauthorized();
            }

            // ---------- 2. Ambiente ----------
            // Evita que un evento de sandbox mueva una venta real (y viceversa).
            var environment = ReadString(root, "environment");
            if (!string.IsNullOrWhiteSpace(environment)
                && !string.Equals(environment, _options.EventEnvironment, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Evento de Wompi del ambiente '{Recibido}' ignorado (la app corre en '{Configurado}').",
                    environment, _options.EventEnvironment);
                return Ok();
            }

            // ---------- 3. Tipo de evento ----------
            var eventName = ReadString(root, "event");
            if (!string.Equals(eventName, TransactionEvent, StringComparison.OrdinalIgnoreCase))
            {
                // 200 para que Wompi no reintente algo que nunca vamos a procesar.
                return Ok();
            }

            // ---------- 4. Transacción ----------
            if (!root.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("transaction", out var transaction))
            {
                _logger.LogWarning("Evento transaction.updated sin objeto 'transaction'. Se ignora.");
                return Ok();
            }

            var snapshot = WompiPaymentGateway.FromTransactionJson(transaction, environment, "Webhook");
            if (snapshot is null)
            {
                _logger.LogWarning("Evento transaction.updated sin referencia utilizable. Se ignora.");
                return Ok();
            }

            // ---------- 5. Aplicar (idempotente y transaccional) ----------
            try
            {
                var result = await _payments.ApplyAsync(snapshot, ct);

                _logger.LogInformation(
                    "Evento de Wompi procesado: pedido {OrderId}, resultado {Resultado}.",
                    result.OrderId, result.Outcome);
            }
            catch (Exception ex)
            {
                // 500 => Wompi reintenta (30 min, 3 h, 24 h).
                _logger.LogError(ex, "Fallo aplicando un evento de Wompi. Se responde 500 para que reintente.");
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        return Ok();
    }

    // ------------------------------------------------------------------ Firma

    /// <summary>
    /// Checksum = SHA-256( valores de signature.properties (en orden) + timestamp + EventsSecret ).
    /// El arreglo <c>properties</c> se recorre DINÁMICAMENTE: la documentación avisa
    /// expresamente de que esa lista puede cambiar.
    /// </summary>
    private bool TryValidateChecksum(JsonElement root, out string motivo)
    {
        motivo = string.Empty;

        if (!root.TryGetProperty("signature", out var signature) || signature.ValueKind != JsonValueKind.Object)
        {
            motivo = "sin objeto signature";
            return false;
        }

        if (!signature.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Array)
        {
            motivo = "sin arreglo signature.properties";
            return false;
        }

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            motivo = "sin objeto data";
            return false;
        }

        if (!root.TryGetProperty("timestamp", out var timestampElement))
        {
            motivo = "sin timestamp";
            return false;
        }

        long timestamp;
        if (timestampElement.ValueKind == JsonValueKind.Number)
        {
            if (!timestampElement.TryGetInt64(out timestamp))
            {
                motivo = "timestamp no entero";
                return false;
            }
        }
        else if (timestampElement.ValueKind != JsonValueKind.String
                 || !long.TryParse(timestampElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out timestamp))
        {
            motivo = "timestamp ilegible";
            return false;
        }

        var values = new List<string>();
        foreach (var property in properties.EnumerateArray())
        {
            var path = property.GetString();
            if (string.IsNullOrWhiteSpace(path))
            {
                motivo = "ruta vacía en signature.properties";
                return false;
            }

            if (!TryResolvePath(data, path!, out var value))
            {
                motivo = $"no se encontró el campo '{path}' dentro de data";
                return false;
            }

            values.Add(value);
        }

        var calculado = WompiSignature.EventChecksum(values, timestamp, _options.EventsSecret);

        // Llega duplicado en el header y en signature.checksum: se acepta cualquiera.
        var recibido = Request.Headers[ChecksumHeader].ToString();
        if (string.IsNullOrWhiteSpace(recibido))
        {
            recibido = ReadString(signature, "checksum") ?? string.Empty;
        }

        if (!WompiSignature.ChecksumMatches(calculado, recibido))
        {
            motivo = "checksum no coincide";
            return false;
        }

        return true;
    }

    /// <summary>Navega una ruta tipo "transaction.amount_in_cents" dentro del objeto data.</summary>
    private static bool TryResolvePath(JsonElement data, string path, out string value)
    {
        value = string.Empty;
        var current = data;

        foreach (var segment in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return false;
            }
        }

        // ⚠️ Los números se concatenan TAL CUAL (GetRawText): formatearlos con cultura
        // convertiría 4490000 en "4.490.000" en es-CO y la firma nunca coincidiría.
        value = current.ValueKind switch
        {
            JsonValueKind.String => current.GetString() ?? string.Empty,
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            _ => current.GetRawText()
        };

        return true;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

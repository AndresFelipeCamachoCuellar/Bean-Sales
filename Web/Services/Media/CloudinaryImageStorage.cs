using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Web.Services.Media;

/// <summary>
/// Implementación real del puerto de almacenamiento. El <c>HttpClient</c> tipado se usa
/// SOLO para borrar recursos (<c>POST /image/destroy</c>): las subidas van directo del
/// navegador al CDN de Cloudinary, así que el archivo NUNCA entra en nuestro proceso
/// (crítico con 256 MB de RAM y un app pool de 32 bits que recicla por inactividad).
/// </summary>
public class CloudinaryImageStorage : IProductImageStorage
{
    private readonly HttpClient _http;
    private readonly CloudinaryOptions _options;
    private readonly ILogger<CloudinaryImageStorage> _logger;

    public CloudinaryImageStorage(
        HttpClient http,
        IOptions<CloudinaryOptions> options,
        ILogger<CloudinaryImageStorage> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool CanUpload => _options.CanUpload;

    public int MaxImagesPerProduct => _options.MaxImagesOrDefault;

    public long MaxFileSizeBytes => _options.MaxFileSizeOrDefault;

    public int MinDimensionPx => _options.MinDimensionOrDefault;

    public IReadOnlyList<string> AllowedFormats => _options.NormalizedFormats;

    public string AcceptAttribute => _options.AcceptAttribute;

    public UploadTicket CreateUploadTicket(Guid productId)
    {
        if (!CanUpload)
        {
            // El controlador corta antes con CanUpload; esto es una red de seguridad.
            throw new InvalidOperationException("Cloudinary no está configurado: no se puede emitir un ticket de subida.");
        }

        // El public_id lo elige el SERVIDOR y va firmado: el navegador no puede subir a
        // la carpeta de otro producto ni sobrescribir un recurso existente (GUID nuevo).
        var publicId = $"{_options.FolderFor(productId)}/{Guid.NewGuid():N}";

        // Invariante a propósito: en es-CO un long se formatearía con separadores de
        // miles y la firma no cuadraría nunca.
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        // SOLO estos parámetros se firman y se envían. Deliberadamente NO se manda
        // `folder` (con public_id de ruta completa, Cloudinary lo prependería y
        // duplicaría la carpeta) ni `overwrite` (el GUID ya garantiza unicidad, y
        // serializar booleanos es una fuente clásica de firmas que no cuadran).
        var toSign = new List<KeyValuePair<string, string?>>
        {
            new("public_id", publicId),
            new("timestamp", timestamp),
            new("allowed_formats", _options.AllowedFormatsCsv)
        };

        var preset = CloudinaryOptions.HasValue(_options.UploadPreset) ? _options.UploadPreset.Trim() : null;
        if (preset is not null)
        {
            toSign.Add(new KeyValuePair<string, string?>("upload_preset", preset));
        }

        var signature = CloudinarySignature.Sign(toSign, _options.ApiSecret);

        var ttl = _options.SignatureTtlSeconds > 0 ? _options.SignatureTtlSeconds : 600;

        return new UploadTicket(
            UploadUrl: _options.UploadUrl,
            ApiKey: _options.ApiKey,
            Timestamp: timestamp,
            Signature: signature,
            PublicId: publicId,
            AllowedFormats: _options.AllowedFormatsCsv,
            UploadPreset: preset,
            MaxBytes: _options.MaxFileSizeOrDefault,
            ExpiresAtUtc: DateTime.UtcNow.AddSeconds(ttl));
    }

    public bool BelongsToProduct(Guid productId, string? publicId)
    {
        if (string.IsNullOrWhiteSpace(publicId))
        {
            return false;
        }

        var id = publicId.Trim();

        // Path traversal y rutas absolutas fuera.
        if (id.Contains("..", StringComparison.Ordinal) || id.StartsWith('/'))
        {
            return false;
        }

        var expectedPrefix = _options.FolderFor(productId) + "/";

        // Debe empezar EXACTAMENTE por la carpeta de este producto y tener algo después.
        return id.StartsWith(expectedPrefix, StringComparison.Ordinal)
               && id.Length > expectedPrefix.Length;
    }

    public bool ResponseSignatureIsValid(CloudinaryUploadResult result)
    {
        if (!_options.VerifyResponseSignature)
        {
            return true;
        }

        // Sin firma o sin versión no hay nada que comparar: no se bloquea por eso (la
        // defensa real es el prefijo del public_id), pero se deja rastro.
        if (string.IsNullOrWhiteSpace(result.Signature) || !result.Version.HasValue)
        {
            _logger.LogWarning(
                "Cloudinary devolvió una subida sin signature/version verificables (public_id {PublicId}).",
                result.PublicId);
            return true;
        }

        var ok = CloudinarySignature.ResponseSignatureMatches(
            result.PublicId, result.Version, result.Signature, _options.ApiSecret);

        if (!ok)
        {
            _logger.LogWarning(
                "La firma de respuesta de Cloudinary NO cuadra para el public_id {PublicId}. " +
                "Si esto se repite con subidas legítimas, desactiva Cloudinary:VerifyResponseSignature en el host.",
                result.PublicId);
        }

        return ok;
    }

    public async Task<bool> DeleteAsync(string publicId, CancellationToken cancellationToken = default)
    {
        if (!CanUpload || string.IsNullOrWhiteSpace(publicId))
        {
            return false;
        }

        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

            // invalidate=true purga el CDN; si no, la imagen borrada puede seguir
            // sirviéndose desde caché durante horas.
            var parameters = new List<KeyValuePair<string, string?>>
            {
                new("public_id", publicId),
                new("timestamp", timestamp),
                new("invalidate", "true")
            };

            var signature = CloudinarySignature.Sign(parameters, _options.ApiSecret);

            var form = new List<KeyValuePair<string, string>>
            {
                new("public_id", publicId),
                new("timestamp", timestamp),
                new("invalidate", "true"),
                new("api_key", _options.ApiKey),
                new("signature", signature)
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.DeleteTimeoutOrDefault));

            using var content = new FormUrlEncodedContent(form);
            using var response = await _http.PostAsync(_options.DestroyUrl, content, cts.Token);

            var body = await response.Content.ReadAsStringAsync(cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                // NO se registra el cuerpo completo por si trajera datos de la cuenta.
                _logger.LogWarning(
                    "Cloudinary respondió {Status} al borrar {PublicId}. Queda un recurso huérfano.",
                    (int)response.StatusCode, publicId);
                return false;
            }

            // La respuesta es { "result": "ok" } o { "result": "not found" }.
            using var json = JsonDocument.Parse(body);
            var result = json.RootElement.TryGetProperty("result", out var r) ? r.GetString() : null;

            if (string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase)
                || string.Equals(result, "not found", StringComparison.OrdinalIgnoreCase))
            {
                // "not found" también es éxito para nosotros: el objetivo era que no exista.
                return true;
            }

            _logger.LogWarning("Cloudinary devolvió result='{Result}' al borrar {PublicId}.", result, publicId);
            return false;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Timeout al borrar {PublicId} en Cloudinary. Queda un recurso huérfano.", publicId);
            return false;
        }
        catch (Exception ex)
        {
            // Un fallo de Cloudinary NUNCA debe romper la operación del usuario.
            _logger.LogWarning(ex, "Error al borrar {PublicId} en Cloudinary. Queda un recurso huérfano.", publicId);
            return false;
        }
    }
}

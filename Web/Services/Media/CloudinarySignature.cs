using System.Security.Cryptography;
using System.Text;

namespace Web.Services.Media;

/// <summary>
/// Criptografía y construcción de URLs de Cloudinary: estática, PURA y sin
/// dependencias (por eso es testeable sin red). Gemela de
/// <see cref="Web.Services.Payments.WompiSignature"/>.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// CONTRATO CONFIRMADO contra el código fuente de los SDK oficiales (jul-2026)
/// cloudinary_npm · lib/utils/index.js  →  api_string_to_sign / api_sign_request
/// ─────────────────────────────────────────────────────────────────────────────
/// 1) FIRMA DE PETICIÓN (upload, destroy, …):
///    a. Se toman los parámetros que van en la petición EXCEPTO
///       <c>file</c>, <c>cloud_name</c>, <c>resource_type</c>, <c>api_key</c> y
///       <c>signature</c> (los tres primeros van en la URL o en el cuerpo del archivo;
///       api_key se AÑADE después de firmar, como hace sign_request del SDK).
///    b. Se descartan los valores null / vacíos (clear_blank).
///    c. Los arreglos se unen con coma.
///    d. Se ordenan por CLAVE (ordinal) y se concatenan como <c>k1=v1&amp;k2=v2</c>.
///    e. Firma versión 2 (la de por defecto hoy): en cada par se escapa el "&amp;"
///       literal como "%26" para impedir "parameter smuggling". Nuestros valores nunca
///       contienen "&amp;", así que v1 y v2 dan la MISMA cadena; se implementa v2 porque
///       es el default del SDK.
///    f. Se pega el <c>api_secret</c> AL FINAL y se hace <b>SHA-1</b> en hexadecimal
///       minúscula (DEFAULT_SIGNATURE_ALGORITHM = "sha1"; el otro soportado es sha256).
///
/// 2) FIRMA DE LA RESPUESTA DE SUBIDA (campo <c>signature</c> del JSON):
///    SDK: verify_api_response_signature(public_id, version) firma
///    <c>{ public_id, version }</c> SIEMPRE con firma versión 1 (sin encoding) y SHA-1.
///    Es decir: SHA-1 hex de <c>public_id={id}&amp;version={v}{api_secret}</c>.
///
/// 3) URL DE ENTREGA:
///    <c>https://res.cloudinary.com/{cloud}/image/upload/{transformación}/v{version}/{public_id}.{formato}</c>
///
/// ⚠️ NUNCA formatear números con cultura al construir estas cadenas: en es-CO
///    1690000000 se convertiría en "1.690.000.000" y la firma jamás coincidiría.
///    Por eso todo entra ya como string invariante.
/// </summary>
public static class CloudinarySignature
{
    /// <summary>Host de entrega (CDN). Siempre https.</summary>
    public const string DeliveryHost = "https://res.cloudinary.com";

    /// <summary>Marcador que separa las transformaciones del resto de la URL de entrega.</summary>
    private const string UploadMarker = "/upload/";

    /// <summary>
    /// Parámetros que NUNCA entran en la firma (van en la URL, en el multipart del
    /// archivo, o se añaden después de firmar).
    /// </summary>
    private static readonly HashSet<string> NotSigned = new(StringComparer.Ordinal)
    {
        "file", "cloud_name", "resource_type", "api_key", "signature"
    };

    /// <summary>
    /// Construye la cadena a firmar (paso a–e). Expuesta aparte de <see cref="Sign"/>
    /// para poder testearla y para poder loguearla en depuración SIN el api_secret.
    /// </summary>
    /// <param name="signatureVersion">2 = escapa "&amp;" como "%26" (default del SDK). 1 = sin escape.</param>
    public static string BuildStringToSign(
        IEnumerable<KeyValuePair<string, string?>> parameters,
        int signatureVersion = 2)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var pairs = parameters
            .Where(p => !NotSigned.Contains(p.Key))
            .Where(p => !string.IsNullOrEmpty(p.Value))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p =>
            {
                var pair = $"{p.Key}={p.Value}";
                return signatureVersion >= 2 ? EncodeParam(pair) : pair;
            });

        return string.Join("&", pairs);
    }

    /// <summary>Firma un diccionario de parámetros. Devuelve SHA-1 hex minúscula.</summary>
    public static string Sign(
        IEnumerable<KeyValuePair<string, string?>> parameters,
        string apiSecret,
        int signatureVersion = 2)
    {
        if (string.IsNullOrEmpty(apiSecret))
        {
            throw new ArgumentException("El api_secret de Cloudinary es obligatorio para firmar.", nameof(apiSecret));
        }

        return Sha1Hex(BuildStringToSign(parameters, signatureVersion) + apiSecret);
    }

    /// <summary>
    /// Verifica la <c>signature</c> que Cloudinary devuelve en la respuesta de subida.
    /// Firma versión 1 y SHA-1 sobre <c>{ public_id, version }</c>.
    /// </summary>
    public static bool ResponseSignatureMatches(
        string? publicId,
        long? version,
        string? receivedSignature,
        string apiSecret)
    {
        if (string.IsNullOrWhiteSpace(publicId)
            || !version.HasValue
            || string.IsNullOrWhiteSpace(receivedSignature)
            || string.IsNullOrEmpty(apiSecret))
        {
            return false;
        }

        var expected = Sign(
            new[]
            {
                new KeyValuePair<string, string?>("public_id", publicId),
                new KeyValuePair<string, string?>("version", version.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
            },
            apiSecret,
            signatureVersion: 1);

        return HashesMatch(expected, receivedSignature);
    }

    /// <summary>
    /// Compara dos hashes hexadecimales: insensible a mayúsculas y en tiempo constante
    /// (no filtra información por temporización).
    /// </summary>
    public static bool HashesMatch(string? expected, string? received)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(received))
        {
            return false;
        }

        var a = expected.Trim();
        var b = received.Trim();

        if (a.Length != b.Length)
        {
            return false;
        }

        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(a),
                Convert.FromHexString(b));
        }
        catch (FormatException)
        {
            // El valor recibido no es hexadecimal: no viene de Cloudinary.
            return false;
        }
    }

    /// <summary>SHA-1 en hexadecimal minúscula del texto UTF-8.</summary>
    public static string Sha1Hex(string value)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Arma una URL de entrega desde cero. Se usa como respaldo cuando no se guardó el
    /// <c>secure_url</c>; en el camino normal se prefiere
    /// <see cref="WithTransformation"/>, que no necesita el cloud name.
    /// </summary>
    public static string BuildDeliveryUrl(
        string cloudName,
        string publicId,
        string? transformation = null,
        long? version = null,
        string? format = null)
    {
        if (string.IsNullOrWhiteSpace(cloudName) || string.IsNullOrWhiteSpace(publicId))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(DeliveryHost)
            .Append('/').Append(cloudName.Trim())
            .Append("/image/upload/");

        if (!string.IsNullOrWhiteSpace(transformation))
        {
            sb.Append(transformation.Trim()).Append('/');
        }

        if (version.HasValue && version.Value > 0)
        {
            sb.Append('v').Append(version.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('/');
        }

        sb.Append(publicId.Trim());

        if (!string.IsNullOrWhiteSpace(format))
        {
            sb.Append('.').Append(format.Trim().TrimStart('.'));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Inserta (o respeta) una transformación en una URL de entrega ya existente
    /// —típicamente el <c>secure_url</c> guardado en la BD—. Es PURA, idempotente y no
    /// necesita credenciales: por eso las vistas pueden derivar cada variante aunque
    /// las llaves se roten o se quiten de la configuración.
    ///
    /// Si la URL no parece de Cloudinary (no contiene "/upload/"), se devuelve tal cual:
    /// así una URL externa pegada a mano en <c>Product.ImageUrl</c> sigue funcionando.
    /// </summary>
    public static string? WithTransformation(string? deliveryUrl, string? transformation)
    {
        if (string.IsNullOrWhiteSpace(deliveryUrl) || string.IsNullOrWhiteSpace(transformation))
        {
            return deliveryUrl;
        }

        var url = deliveryUrl.Trim();

        // Mixed content: el CDN sirve https siempre; si alguna URL vieja quedó en http, se corrige.
        if (url.StartsWith("http://res.cloudinary.com", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url.Substring("http://".Length);
        }

        var index = url.IndexOf(UploadMarker, StringComparison.Ordinal);
        if (index < 0)
        {
            return url;
        }

        var insertAt = index + UploadMarker.Length;
        var rest = url.Substring(insertAt);
        var t = transformation.Trim();

        // Idempotente: si ya trae exactamente esta transformación, no se duplica.
        if (rest.StartsWith(t + "/", StringComparison.Ordinal))
        {
            return url;
        }

        return string.Concat(url.AsSpan(0, insertAt), t, "/", rest);
    }

    /// <summary>
    /// Escape de la firma versión 2: solo el "&amp;" literal, como
    /// <c>encode_param</c> del SDK oficial. NO es un url-encode completo.
    /// </summary>
    private static string EncodeParam(string value) => value.Replace("&", "%26", StringComparison.Ordinal);
}

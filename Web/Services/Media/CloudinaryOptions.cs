using System.Diagnostics.CodeAnalysis;

namespace Web.Services.Media;

/// <summary>
/// Configuración de la galería de fotos (sección "Cloudinary" de appsettings). Mismo
/// patrón que <see cref="Web.Services.Shipping.ShippingOptions"/> y
/// <see cref="Web.Services.Payments.WompiOptions"/>: la FORMA está en el repo, los
/// VALORES no.
///
/// Clasificación de las 3 credenciales:
///   · CloudName — NO es secreto (aparece en cada URL de entrega).
///   · ApiKey    — NO es secreto (por diseño viaja al navegador en cada subida firmada).
///   · ApiSecret — 🔒 SECRETO. Jamás sale del backend, ni al ViewModel, ni a un log.
///
/// Dónde viven los valores reales:
///   · Dev:        Web/appsettings.Development.json (gitignored, untracked).
///   · Producción: GitHub Secrets CLOUDINARY_CLOUD_NAME / _API_KEY / _API_SECRET,
///                 inyectados por .github/workflows/deploy.yml en el appsettings.json
///                 publicado (inyección defensiva).
/// </summary>
public class CloudinaryOptions
{
    /// <summary>
    /// Interruptor general. En false (o sin credenciales) el gestor de fotos queda en
    /// modo lectura y el sitio se comporta EXACTAMENTE como antes de este incremento:
    /// el catálogo usa el patrón de granos y los formularios de producto guardan igual.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public string CloudName { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>SECRETO. Firma los tickets de subida y las peticiones de borrado.</summary>
    public string ApiSecret { get; set; } = string.Empty;

    /// <summary>
    /// Prefijo de carpeta de todos los recursos. El public_id final es
    /// <c>{Folder}/{ProductID:N}/{Guid:N}</c>, así que cada lote queda aislado en su
    /// propia carpeta (auditable y fácil de limpiar).
    /// </summary>
    public string Folder { get; set; } = "bean/products";

    /// <summary>
    /// Upload preset OPCIONAL, en modo <b>Signed</b>. Es el segundo cinturón de
    /// seguridad (ahí vive <c>max_file_size</c>). Por defecto VACÍO a propósito: si se
    /// pone el nombre de un preset que no existe (o que define su propio <c>folder</c>),
    /// Cloudinary rechaza la subida. Solo llenarlo cuando el preset esté creado.
    /// ⚠️ NUNCA usar un preset Unsigned: expuesto en el HTML, cualquiera podría subir
    /// a la cuenta y agotar los créditos.
    /// </summary>
    public string UploadPreset { get; set; } = string.Empty;

    /// <summary>Host del API de Cloudinary. Configurable solo por si algún día cambia.</summary>
    public string ApiBaseUrl { get; set; } = "https://api.cloudinary.com";

    public int MaxImagesPerProduct { get; set; } = 6;

    /// <summary>5 MB. Se valida en el cliente (UX) y en el registro (autoridad).</summary>
    public long MaxFileSizeBytes { get; set; } = 5_242_880;

    /// <summary>Rechaza fotos minúsculas. Cloudinary ya nos devuelve las dimensiones.</summary>
    public int MinDimensionPx { get; set; } = 800;

    public string[] AllowedFormats { get; set; } = new[] { "jpg", "jpeg", "png", "webp" };

    /// <summary>Vigencia del ticket en el cliente (informativa: la ventana real la aplica Cloudinary).</summary>
    public int SignatureTtlSeconds { get; set; } = 600;

    /// <summary>Timeout duro del borrado. Un fallo de Cloudinary NUNCA bloquea al usuario.</summary>
    public int DeleteTimeoutSeconds { get; set; } = 6;

    /// <summary>
    /// Verificar la <c>signature</c> que Cloudinary devuelve en la respuesta de subida.
    /// Receta confirmada contra el SDK oficial: SHA-1 de
    /// <c>public_id={id}&amp;version={v}{ApiSecret}</c> (firma versión 1, sin encoding).
    /// Se deja como interruptor para poder desactivarlo desde el host sin recompilar
    /// si alguna cuenta se comportara distinto.
    /// </summary>
    public bool VerifyResponseSignature { get; set; } = true;

    /// <summary>Tickets de subida por usuario y por minuto. Única defensa contra un usuario legítimo que quema la cuota.</summary>
    public int MaxTicketsPerMinute { get; set; } = 20;

    // ------------------------------------------------------------------ Derivados

    /// <summary>
    /// ¿Se puede subir? Hacen falta las 3 credenciales: sin CloudName no hay destino,
    /// sin ApiKey Cloudinary no identifica la cuenta y sin ApiSecret no hay firma.
    /// </summary>
    public bool CanUpload => Enabled && HasValue(CloudName) && HasValue(ApiKey) && HasValue(ApiSecret);

    public int MaxImagesOrDefault => MaxImagesPerProduct > 0 ? MaxImagesPerProduct : 6;

    public long MaxFileSizeOrDefault => MaxFileSizeBytes > 0 ? MaxFileSizeBytes : 5_242_880;

    public int MinDimensionOrDefault => MinDimensionPx > 0 ? MinDimensionPx : 0;

    public int DeleteTimeoutOrDefault => DeleteTimeoutSeconds > 0 ? DeleteTimeoutSeconds : 6;

    public int MaxTicketsPerMinuteOrDefault => MaxTicketsPerMinute > 0 ? MaxTicketsPerMinute : 20;

    /// <summary>Tamaño máximo en MB, redondeado, para los textos de la interfaz.</summary>
    public double MaxFileSizeMb => Math.Round(MaxFileSizeOrDefault / 1024d / 1024d, 1);

    public string FolderOrDefault =>
        string.IsNullOrWhiteSpace(Folder) ? "bean/products" : Folder.Trim().Trim('/');

    /// <summary>Formatos en minúscula, sin punto, sin duplicados y sin vacíos.</summary>
    public IReadOnlyList<string> NormalizedFormats
    {
        get
        {
            var source = AllowedFormats is { Length: > 0 }
                ? AllowedFormats
                : new[] { "jpg", "jpeg", "png", "webp" };

            return source
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(f => f.Trim().TrimStart('.').ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
    }

    /// <summary>Valor del parámetro firmado <c>allowed_formats</c>: "jpg,jpeg,png,webp".</summary>
    public string AllowedFormatsCsv => string.Join(",", NormalizedFormats);

    /// <summary>Atributo <c>accept</c> del input de archivo (UX; la autoridad es allowed_formats).</summary>
    public string AcceptAttribute => string.Join(",", NormalizedFormats.Select(f => f switch
    {
        "jpg" or "jpeg" => "image/jpeg",
        "png" => "image/png",
        "webp" => "image/webp",
        "gif" => "image/gif",
        "avif" => "image/avif",
        _ => "image/" + f
    }).Distinct(StringComparer.Ordinal));

    /// <summary>Endpoint de subida: POST multipart desde el NAVEGADOR.</summary>
    public string UploadUrl => $"{ApiBaseUrl.TrimEnd('/')}/v1_1/{CloudName}/image/upload";

    /// <summary>Endpoint de borrado: POST desde NUESTRO backend (necesita firma).</summary>
    public string DestroyUrl => $"{ApiBaseUrl.TrimEnd('/')}/v1_1/{CloudName}/image/destroy";

    /// <summary>Carpeta de un producto concreto (prefijo obligatorio de sus public_id).</summary>
    public string FolderFor(Guid productId) => $"{FolderOrDefault}/{productId:N}";

    /// <summary>
    /// Un valor cuenta como cargado si no está vacío y no es uno de los marcadores
    /// "PEGA_AQUI_…" de la plantilla de configuración local.
    /// </summary>
    public static bool HasValue([NotNullWhen(true)] string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.TrimStart().StartsWith("PEGA_AQUI", StringComparison.OrdinalIgnoreCase);
}

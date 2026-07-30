namespace Web.Services.Media;

/// <summary>
/// Variantes fijas de entrega. Son CUATRO a propósito: en el plan Free de Cloudinary
/// cada combinación nueva de parámetros es una transformación facturable (1 crédito =
/// 1.000 transformaciones), y luego se sirve de caché del CDN. Un <c>srcset</c> con 5
/// anchos multiplicaría el gasto por 5 sin beneficio perceptible en tarjetas de 150 px.
/// </summary>
public enum ImageVariant
{
    /// <summary>Tarjeta del catálogo, thumbs del carrito y de los pedidos, Orígenes.</summary>
    Catalog = 0,

    /// <summary>Imagen principal del detalle (frame 1:1).</summary>
    Main = 1,

    /// <summary>Miniaturas de 74 px del detalle, a 2x para pantallas retina.</summary>
    Thumb = 2,

    /// <summary>Tarjetas del gestor de fotos y miniaturas del panel.</summary>
    Admin = 3
}

/// <summary>
/// Transformaciones por URL. <c>f_auto</c> entrega AVIF/WebP/JPEG según el navegador y
/// <c>q_auto</c> elige la calidad óptima: menos bytes = más velocidad en móvil y menos
/// créditos de ancho de banda.
/// </summary>
public static class ImageTransformations
{
    public const string Catalog = "c_fill,w_640,h_420,f_auto,q_auto";
    public const string Main = "c_fill,w_900,h_900,f_auto,q_auto";
    public const string Thumb = "c_fill,w_148,h_148,f_auto,q_auto";
    public const string Admin = "c_fill,w_240,h_240,f_auto,q_auto";

    public static string For(ImageVariant variant) => variant switch
    {
        ImageVariant.Main => Main,
        ImageVariant.Thumb => Thumb,
        ImageVariant.Admin => Admin,
        _ => Catalog
    };
}

/// <summary>
/// Ticket de subida: todo lo que el navegador necesita para hablar DIRECTAMENTE con
/// Cloudinary. <b>Jamás incluye el api_secret.</b> Los parámetros firmados son
/// inmutables: si el JS altera cualquiera, Cloudinary rechaza la subida.
/// </summary>
public sealed record UploadTicket(
    string UploadUrl,
    string ApiKey,
    string Timestamp,
    string Signature,
    string PublicId,
    string AllowedFormats,
    string? UploadPreset,
    long MaxBytes,
    DateTime ExpiresAtUtc);

/// <summary>
/// Lo que Cloudinary devuelve tras una subida y el navegador nos reenvía para registrar.
///
/// ⚠️ Todo esto llega DEL CLIENTE y por tanto no es de fiar. Se valida en
/// <c>ProductImageService.RegisterAsync</c>: <see cref="PublicId"/> contra el prefijo que
/// firmamos, <see cref="Signature"/> contra el api_secret, y el resto contra las cuotas.
/// <see cref="SecureUrl"/> se recibe solo para diagnóstico: la URL que se PERSISTE se
/// deriva en el servidor, nunca se copia de aquí.
/// </summary>
public sealed record CloudinaryUploadResult(
    string PublicId,
    long? Version,
    string? Format,
    int? Width,
    int? Height,
    long? Bytes,
    string? SecureUrl,
    string? Signature);

/// <summary>
/// Puerto de almacenamiento de imágenes. Dos implementaciones:
/// <see cref="CloudinaryImageStorage"/> (real) y <see cref="DisabledImageStorage"/>
/// (degradación segura sin credenciales), igual que
/// <c>FixedShippingQuoteService</c> es la red de seguridad del envío.
/// </summary>
public interface IProductImageStorage
{
    /// <summary>¿Hay credenciales cargadas? Si es false, el gestor queda en modo lectura.</summary>
    bool CanUpload { get; }

    /// <summary>Máximo de fotos por producto (para la interfaz y la validación de cuota).</summary>
    int MaxImagesPerProduct { get; }

    /// <summary>Tamaño máximo por archivo, en bytes.</summary>
    long MaxFileSizeBytes { get; }

    /// <summary>Lado mínimo aceptado en píxeles (0 = sin mínimo).</summary>
    int MinDimensionPx { get; }

    /// <summary>Formatos aceptados, en minúscula y sin punto.</summary>
    IReadOnlyList<string> AllowedFormats { get; }

    /// <summary>Valor del atributo <c>accept</c> del input de archivo.</summary>
    string AcceptAttribute { get; }

    /// <summary>
    /// Firma un ticket para UN archivo de UN producto. No toca la red: es un SHA-1.
    /// El <c>public_id</c> lo elige el SERVIDOR (carpeta del producto + GUID nuevo).
    /// </summary>
    UploadTicket CreateUploadTicket(Guid productId);

    /// <summary>
    /// ¿El <c>public_id</c> que dice el navegador pertenece de verdad a este producto?
    /// Es la defensa que importa: sin esto alguien podría registrar un recurso ajeno.
    /// </summary>
    bool BelongsToProduct(Guid productId, string? publicId);

    /// <summary>
    /// Verifica la firma de la respuesta de subida. Devuelve true si la verificación
    /// está desactivada por configuración o si no hay datos suficientes para juzgar.
    /// </summary>
    bool ResponseSignatureIsValid(CloudinaryUploadResult result);

    /// <summary>
    /// Borra el recurso en Cloudinary. <b>NUNCA lanza</b>: devuelve false y loguea, para
    /// que un fallo del proveedor no rompa la operación del usuario.
    /// </summary>
    Task<bool> DeleteAsync(string publicId, CancellationToken cancellationToken = default);
}

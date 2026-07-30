using Web.Models;

namespace Web.Services.Media;

/// <summary>
/// Deriva la URL de cada variante a partir del <c>secure_url</c> guardado en la fila.
/// Es PURA y no necesita credenciales, por dos razones prácticas:
///
///  1. Las VISTAS pueden pedir cualquier variante sin inyectar servicios ni exponer el
///     cloud name / api key en Razor.
///  2. Si algún día se rotan o se quitan las llaves de la configuración, las fotos ya
///     subidas <b>siguen mostrándose</b> (la entrega por CDN no requiere autenticación).
///
/// Respaldo: si la fila no tiene <c>secure_url</c> (subida antigua o parcial), se puede
/// reconstruir con <see cref="CloudinarySignature.BuildDeliveryUrl"/> pasando el cloud
/// name; ese camino solo lo usa el servicio de aplicación.
/// </summary>
public static class ProductImageUrls
{
    /// <summary>URL de una imagen en la variante pedida. Devuelve null si no hay base de la que derivar.</summary>
    public static string? For(ProductImage? image, ImageVariant variant)
    {
        if (image is null || string.IsNullOrWhiteSpace(image.SecureUrl))
        {
            return null;
        }

        return CloudinarySignature.WithTransformation(image.SecureUrl, ImageTransformations.For(variant));
    }

    /// <summary>
    /// Portada de una colección ya materializada (no dispara consultas nuevas):
    /// la marcada como <c>IsCover</c> o, si no hay ninguna, la primera por orden.
    /// </summary>
    public static ProductImage? Cover(IEnumerable<ProductImage>? images)
    {
        if (images is null)
        {
            return null;
        }

        var active = images.Where(i => i.Status).ToList();

        return active.FirstOrDefault(i => i.IsCover) ?? active.OrderBy(i => i.SortOrder).FirstOrDefault();
    }

    /// <summary>Imágenes activas ordenadas para la galería.</summary>
    public static List<ProductImage> Gallery(IEnumerable<ProductImage>? images) =>
        images is null
            ? new List<ProductImage>()
            : images.Where(i => i.Status).OrderBy(i => i.SortOrder).ToList();

    /// <summary>
    /// Texto alternativo listo para el atributo <c>alt</c>: el que puso quien subió la
    /// foto o, si está vacío, el nombre del producto.
    /// </summary>
    public static string AltFor(ProductImage? image, string? productName) =>
        !string.IsNullOrWhiteSpace(image?.AltText)
            ? image!.AltText!
            : (string.IsNullOrWhiteSpace(productName) ? "Foto del lote" : $"Foto de {productName}");
}

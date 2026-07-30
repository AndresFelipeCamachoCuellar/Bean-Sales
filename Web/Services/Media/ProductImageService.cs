using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Web.Data;
using Web.Models;
using Web.Models.Enums;
using Web.Models.ViewModels;

namespace Web.Services.Media;

/// <summary>Resultado uniforme de cualquier mutación de la galería.</summary>
/// <param name="Ok">¿Se aplicó el cambio?</param>
/// <param name="Error">Mensaje EN ESPAÑOL y concreto para mostrar al usuario.</param>
/// <param name="Images">Estado completo de la galería tras el cambio (el cliente re-renderiza).</param>
/// <param name="CoverUrl">Portada en variante de catálogo, o null si el lote quedó sin fotos.</param>
public sealed record ProductImageResult(
    bool Ok,
    string? Error,
    List<ProductImageItemViewModel> Images,
    string? CoverUrl)
{
    public static ProductImageResult Fail(string error) =>
        new(false, error, new List<ProductImageItemViewModel>(), null);
}

/// <summary>
/// Reglas de negocio de la galería. Centralizarlas aquí evita que el controlador y las
/// vistas las repitan (y que se desincronicen).
///
/// Invariantes que garantiza:
///  1. Si hay ≥1 imagen activa, EXACTAMENTE una tiene <c>IsCover = true</c>.
///  2. <c>SortOrder</c> queda compacto (0..n-1) tras cualquier borrado o reordenamiento.
///  3. <c>Product.ImageUrl</c> queda sincronizado con la portada (variante de catálogo)
///     después de CADA operación. Esto es lo que mantiene intactas las 6 vistas que ya
///     consumen ImageUrl —incluidas carrito, checkout y confirmación—.
///  4. Cuota: nunca más de <c>MaxImagesPerProduct</c> activas.
///  5. Calidad mínima: se rechaza (y se borra de Cloudinary) lo que no llegue al mínimo.
///  6. Al borrar se marca <c>Status = false</c> en BD PRIMERO y se intenta borrar en
///     Cloudinary después: si el proveedor falla, la foto ya desapareció del sitio y
///     solo queda un recurso huérfano + un LogWarning. Nunca se rompe al usuario.
/// </summary>
public class ProductImageService
{
    private readonly ApplicationDbContext _context;
    private readonly IProductImageStorage _storage;
    private readonly CloudinaryOptions _options;
    private readonly ILogger<ProductImageService> _logger;

    public ProductImageService(
        ApplicationDbContext context,
        IProductImageStorage storage,
        IOptions<CloudinaryOptions> options,
        ILogger<ProductImageService> logger)
    {
        _context = context;
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    // ------------------------------------------------------------------ Lectura

    /// <summary>Cuántas fotos activas tiene el lote.</summary>
    public Task<int> CountAsync(Guid productId, CancellationToken cancellationToken = default) =>
        _context.ProductImages.CountAsync(i => i.ProductID == productId && i.Status, cancellationToken);

    /// <summary>Galería activa, ordenada.</summary>
    public Task<List<ProductImage>> ListAsync(Guid productId, CancellationToken cancellationToken = default) =>
        _context.ProductImages
            .Where(i => i.ProductID == productId && i.Status)
            .OrderBy(i => i.SortOrder)
            .ToListAsync(cancellationToken);

    /// <summary>Arma el ViewModel de la partial del gestor.</summary>
    public async Task<ProductImageManagerViewModel> BuildManagerAsync(
        Product product,
        bool canEdit,
        ImageUploader role,
        CancellationToken cancellationToken = default)
    {
        var images = await ListAsync(product.ProductID, cancellationToken);

        return new ProductImageManagerViewModel
        {
            ProductID = product.ProductID,
            ProductName = product.Name,
            Images = images.Select(ToItem).ToList(),
            CanEdit = canEdit,
            StorageEnabled = _storage.CanUpload,
            Role = role,
            MaxImages = _storage.MaxImagesPerProduct,
            MaxFileSizeBytes = _storage.MaxFileSizeBytes,
            MinDimensionPx = _storage.MinDimensionPx,
            AllowedFormats = _storage.AllowedFormats.ToList(),
            AcceptAttribute = _storage.AcceptAttribute
        };
    }

    // ------------------------------------------------------------------ Registro

    /// <summary>
    /// Registra en BD una foto que el navegador YA subió a Cloudinary. Es el punto donde
    /// se aplican las validaciones que el cliente no puede garantizar. Si algo falla, se
    /// intenta borrar el recurso recién subido para no dejar basura.
    /// </summary>
    public async Task<ProductImageResult> RegisterAsync(
        Guid productId,
        CloudinaryUploadResult upload,
        ImageUploader uploadedByRole,
        string uploadedBy,
        CancellationToken cancellationToken = default)
    {
        // 1. El public_id debe pertenecer a ESTE producto (prefijo que firmamos nosotros).
        if (!_storage.BelongsToProduct(productId, upload.PublicId))
        {
            _logger.LogWarning(
                "Se intentó registrar el public_id '{PublicId}' en el producto {ProductID}, al que no pertenece.",
                upload.PublicId, productId);
            return ProductImageResult.Fail("La foto no corresponde a este lote. Vuelve a intentarlo.");
        }

        // 2. Firma de la respuesta de Cloudinary (confirma que el JSON no lo inventó el navegador).
        if (!_storage.ResponseSignatureIsValid(upload))
        {
            await TryDeleteRemoteAsync(upload.PublicId, cancellationToken);
            return ProductImageResult.Fail("No pudimos verificar la foto con Cloudinary. Vuelve a intentarlo.");
        }

        // 3. Formato.
        var format = (upload.Format ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
        if (format.Length == 0 || !_storage.AllowedFormats.Contains(format, StringComparer.Ordinal))
        {
            await TryDeleteRemoteAsync(upload.PublicId, cancellationToken);
            return ProductImageResult.Fail($"Solo aceptamos {FormatsLabel()}.");
        }

        // 4. Tamaño.
        if (upload.Bytes.HasValue && upload.Bytes.Value > _storage.MaxFileSizeBytes)
        {
            await TryDeleteRemoteAsync(upload.PublicId, cancellationToken);
            var mb = Math.Round(upload.Bytes.Value / 1024d / 1024d, 1);
            var max = Math.Round(_storage.MaxFileSizeBytes / 1024d / 1024d, 1);
            return ProductImageResult.Fail($"La foto pesa {mb} MB y el máximo es {max} MB.");
        }

        // 5. Resolución mínima (Cloudinary ya nos devolvió las dimensiones: es gratis validarlo).
        var min = _storage.MinDimensionPx;
        if (min > 0 && upload.Width.HasValue && upload.Height.HasValue
            && (upload.Width.Value < min || upload.Height.Value < min))
        {
            await TryDeleteRemoteAsync(upload.PublicId, cancellationToken);
            return ProductImageResult.Fail(
                $"La foto es de {upload.Width.Value}×{upload.Height.Value} px; necesitamos al menos {min}×{min}.");
        }

        // 6. Doble registro (el índice único en PublicId también lo impide en BD).
        var alreadyRegistered = await _context.ProductImages
            .AnyAsync(i => i.PublicId == upload.PublicId, cancellationToken);

        if (alreadyRegistered)
        {
            // No se borra en Cloudinary: la fila que ya existe lo está usando.
            return await BuildResultAsync(productId, cancellationToken);
        }

        // 7. Cuota (se re-valida aquí, no solo al emitir el ticket).
        var current = await ListAsync(productId, cancellationToken);
        if (current.Count >= _storage.MaxImagesPerProduct)
        {
            await TryDeleteRemoteAsync(upload.PublicId, cancellationToken);
            return ProductImageResult.Fail(
                $"Llegaste al máximo de {_storage.MaxImagesPerProduct} fotos. Elimina una para subir otra.");
        }

        // ⚠️ SEGURIDAD: la URL se DERIVA en el servidor, nunca se toma del navegador.
        // El secure_url que reporta el cliente es una cadena arbitraria que después se
        // interpola en atributos style/onclick de las vistas: aceptarla tal cual sería
        // un vector de inyección (una comilla basta para salir del literal). Todas las
        // piezas de aquí son seguras: CloudName viene de configuración, el public_id lo
        // generó y firmó el servidor, el formato está en la lista blanca y la versión es
        // un entero. El resultado es idéntico al secure_url canónico de Cloudinary.
        var secureUrl = CloudinarySignature.BuildDeliveryUrl(
            _options.CloudName, upload.PublicId, null, upload.Version, format);

        if (string.IsNullOrWhiteSpace(secureUrl))
        {
            // Solo puede pasar si falta CloudName, y en ese caso CanUpload era false.
            await TryDeleteRemoteAsync(upload.PublicId, cancellationToken);
            return ProductImageResult.Fail("La galería de fotos no está bien configurada.");
        }

        var image = new ProductImage
        {
            ProductImageID = Guid.NewGuid(),
            ProductID = productId,
            PublicId = upload.PublicId.Trim(),
            Version = upload.Version,
            Format = format,
            Width = upload.Width,
            Height = upload.Height,
            Bytes = upload.Bytes,
            SecureUrl = Truncate(secureUrl, 500),
            StorageProvider = "Cloudinary",
            SortOrder = current.Count,
            IsCover = current.Count == 0,       // la primera foto nace como portada
            UploadedByRole = uploadedByRole,
            UploadedBy = string.IsNullOrWhiteSpace(uploadedBy) ? "SYSTEM" : uploadedBy,
            UploadedOn = DateTime.Now,
            Status = true
        };

        _context.ProductImages.Add(image);
        await _context.SaveChangesAsync(cancellationToken);

        await NormalizeAsync(productId, cancellationToken);

        return await BuildResultAsync(productId, cancellationToken);
    }

    // ------------------------------------------------------------------ Mutaciones

    /// <summary>Borra una foto (soft delete en BD + destroy en Cloudinary, en ese orden).</summary>
    public async Task<ProductImageResult> DeleteAsync(
        Guid productId,
        Guid imageId,
        CancellationToken cancellationToken = default)
    {
        var image = await _context.ProductImages
            .FirstOrDefaultAsync(i => i.ProductImageID == imageId && i.ProductID == productId && i.Status, cancellationToken);

        if (image is null)
        {
            return ProductImageResult.Fail("Esa foto ya no existe.");
        }

        image.Status = false;
        image.IsCover = false;
        await _context.SaveChangesAsync(cancellationToken);

        // Desde aquí la foto ya no se ve en el sitio. Si Cloudinary falla, solo queda
        // un huérfano confinado en la carpeta del producto.
        await TryDeleteRemoteAsync(image.PublicId, cancellationToken);

        await NormalizeAsync(productId, cancellationToken);

        return await BuildResultAsync(productId, cancellationToken);
    }

    /// <summary>Reordena la galería según la lista de ids recibida. Los ids no incluidos van al final.</summary>
    public async Task<ProductImageResult> ReorderAsync(
        Guid productId,
        IList<Guid> orderedIds,
        CancellationToken cancellationToken = default)
    {
        var images = await ListAsync(productId, cancellationToken);
        if (images.Count == 0)
        {
            return await BuildResultAsync(productId, cancellationToken);
        }

        var requested = (orderedIds ?? new List<Guid>())
            .Distinct()
            .Where(id => images.Any(i => i.ProductImageID == id))
            .ToList();

        var order = 0;
        foreach (var id in requested)
        {
            var image = images.First(i => i.ProductImageID == id);
            image.SortOrder = order++;
        }

        // Lo que el cliente no mencionó conserva su orden relativo, detrás.
        foreach (var image in images.Where(i => !requested.Contains(i.ProductImageID)).OrderBy(i => i.SortOrder))
        {
            image.SortOrder = order++;
        }

        await _context.SaveChangesAsync(cancellationToken);
        await NormalizeAsync(productId, cancellationToken);

        return await BuildResultAsync(productId, cancellationToken);
    }

    /// <summary>Marca una foto como portada (y desmarca el resto).</summary>
    public async Task<ProductImageResult> SetCoverAsync(
        Guid productId,
        Guid imageId,
        CancellationToken cancellationToken = default)
    {
        var images = await ListAsync(productId, cancellationToken);

        if (images.All(i => i.ProductImageID != imageId))
        {
            return ProductImageResult.Fail("Esa foto ya no existe.");
        }

        foreach (var image in images)
        {
            image.IsCover = image.ProductImageID == imageId;
        }

        await _context.SaveChangesAsync(cancellationToken);
        await SyncCoverUrlAsync(productId, cancellationToken);

        return await BuildResultAsync(productId, cancellationToken);
    }

    /// <summary>Guarda el texto alternativo (accesibilidad y SEO).</summary>
    public async Task<ProductImageResult> SetAltTextAsync(
        Guid productId,
        Guid imageId,
        string? altText,
        CancellationToken cancellationToken = default)
    {
        var image = await _context.ProductImages
            .FirstOrDefaultAsync(i => i.ProductImageID == imageId && i.ProductID == productId && i.Status, cancellationToken);

        if (image is null)
        {
            return ProductImageResult.Fail("Esa foto ya no existe.");
        }

        image.AltText = string.IsNullOrWhiteSpace(altText) ? null : Truncate(altText.Trim(), 200);
        await _context.SaveChangesAsync(cancellationToken);

        return await BuildResultAsync(productId, cancellationToken);
    }

    /// <summary>
    /// Borra TODAS las fotos de un producto, en BD y en Cloudinary. Hoy nadie la llama:
    /// el borrado de productos es SOFT (<c>Product.Status = false</c>) y las fotos deben
    /// sobrevivir para poder reactivar el lote. Existe para el día en que haya borrado
    /// duro, porque la cascada de la FK limpia la BD pero NO la nube.
    /// </summary>
    public async Task DeleteAllForProductAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var images = await _context.ProductImages
            .Where(i => i.ProductID == productId)
            .ToListAsync(cancellationToken);

        foreach (var image in images)
        {
            await TryDeleteRemoteAsync(image.PublicId, cancellationToken);
        }

        _context.ProductImages.RemoveRange(images);
        await _context.SaveChangesAsync(cancellationToken);

        await SyncCoverUrlAsync(productId, cancellationToken);
    }

    // ------------------------------------------------------------------ Internos

    /// <summary>Compacta SortOrder, garantiza una única portada y sincroniza Product.ImageUrl.</summary>
    private async Task NormalizeAsync(Guid productId, CancellationToken cancellationToken)
    {
        var images = await ListAsync(productId, cancellationToken);
        var dirty = false;

        for (var i = 0; i < images.Count; i++)
        {
            if (images[i].SortOrder != i)
            {
                images[i].SortOrder = i;
                dirty = true;
            }
        }

        var covers = images.Where(i => i.IsCover).ToList();

        if (images.Count > 0 && covers.Count != 1)
        {
            // 0 portadas (se borró la que había) o >1 (estado inconsistente): la hereda
            // la primera por orden.
            foreach (var image in images)
            {
                var shouldBeCover = image.ProductImageID == images[0].ProductImageID;
                if (image.IsCover != shouldBeCover)
                {
                    image.IsCover = shouldBeCover;
                    dirty = true;
                }
            }
        }

        if (dirty)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        await SyncCoverUrlAsync(productId, cancellationToken);
    }

    /// <summary>
    /// Escribe en <c>Product.ImageUrl</c> la portada ya transformada a variante de
    /// catálogo. Es el contrato con las 6 vistas que no cargan la galería.
    ///
    /// Si el lote se queda sin fotos y la URL actual NO es de Cloudinary, se RESPETA:
    /// puede ser una URL externa que alguien pegó a mano antes de este incremento.
    /// </summary>
    private async Task SyncCoverUrlAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductID == productId, cancellationToken);
        if (product is null)
        {
            return;
        }

        var images = await ListAsync(productId, cancellationToken);
        var cover = ProductImageUrls.Cover(images);

        string? desired;

        if (cover is not null)
        {
            desired = ProductImageUrls.For(cover, ImageVariant.Catalog)
                      ?? CloudinarySignature.BuildDeliveryUrl(
                             _options.CloudName, cover.PublicId,
                             ImageTransformations.Catalog, cover.Version, cover.Format);

            if (string.IsNullOrWhiteSpace(desired))
            {
                // Sin cloud name y sin secure_url no hay nada que escribir: se deja como está.
                return;
            }
        }
        else
        {
            var current = product.ImageUrl;
            var currentIsCloudinary = !string.IsNullOrWhiteSpace(current)
                && current.Contains("res.cloudinary.com", StringComparison.OrdinalIgnoreCase);

            // Sin fotos: se limpia solo si lo que había lo puso esta galería.
            desired = currentIsCloudinary ? null : current;
        }

        if (!string.Equals(product.ImageUrl, desired, StringComparison.Ordinal))
        {
            product.ImageUrl = desired;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<ProductImageResult> BuildResultAsync(Guid productId, CancellationToken cancellationToken)
    {
        var images = await ListAsync(productId, cancellationToken);
        var cover = ProductImageUrls.Cover(images);

        return new ProductImageResult(
            true,
            null,
            images.Select(ToItem).ToList(),
            ProductImageUrls.For(cover, ImageVariant.Catalog));
    }

    private async Task TryDeleteRemoteAsync(string publicId, CancellationToken cancellationToken)
    {
        var ok = await _storage.DeleteAsync(publicId, cancellationToken);
        if (!ok)
        {
            _logger.LogWarning(
                "No se pudo borrar '{PublicId}' en Cloudinary; queda huérfano en la carpeta del producto.",
                publicId);
        }
    }

    private static ProductImageItemViewModel ToItem(ProductImage image) => new()
    {
        Id = image.ProductImageID,
        ThumbUrl = ProductImageUrls.For(image, ImageVariant.Admin),
        FullUrl = ProductImageUrls.For(image, ImageVariant.Main),
        IsCover = image.IsCover,
        SortOrder = image.SortOrder,
        AltText = image.AltText,
        UploadedByLabel = image.UploadedByRole == ImageUploader.Admin ? "Bean" : "Proveedor",
        UploadedOn = image.UploadedOn.ToString("dd MMM yyyy", new System.Globalization.CultureInfo("es-CO")),
        Dimensions = BuildDimensions(image)
    };

    private static string? BuildDimensions(ProductImage image)
    {
        var parts = new List<string>();

        if (image.Width.HasValue && image.Height.HasValue)
        {
            parts.Add($"{image.Width.Value} × {image.Height.Value}");
        }

        if (image.Bytes.HasValue && image.Bytes.Value > 0)
        {
            var kb = image.Bytes.Value / 1024d;
            parts.Add(kb >= 1024
                ? $"{Math.Round(kb / 1024d, 1)} MB"
                : $"{Math.Round(kb)} KB");
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private string FormatsLabel()
    {
        var labels = _storage.AllowedFormats
            .Select(f => f == "jpeg" ? "JPG" : f.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (labels.Count <= 1)
        {
            return labels.FirstOrDefault() ?? "JPG, PNG o WebP";
        }

        return string.Join(", ", labels.Take(labels.Count - 1)) + " o " + labels[^1];
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max);
}

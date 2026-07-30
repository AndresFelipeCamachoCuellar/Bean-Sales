using Web.Models.Enums;

namespace Web.Models.ViewModels;

/// <summary>
/// Todo lo que necesita la partial <c>_ProductImageManager</c>. Se construye en el
/// servidor (<c>ProductImageService.BuildManagerAsync</c>) para que la vista no tenga
/// que conocer las reglas de cuota, formatos ni transformaciones.
///
/// ⚠️ Aquí NO viaja el api_secret de Cloudinary. Ni siquiera el api_key: ese se entrega
/// solo en el ticket firmado, y solo tras autorizar al usuario sobre este producto.
/// </summary>
public class ProductImageManagerViewModel
{
    public Guid ProductID { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public List<ProductImageItemViewModel> Images { get; set; } = new();

    /// <summary>¿Este usuario puede subir/borrar/reordenar fotos de este lote?</summary>
    public bool CanEdit { get; set; }

    /// <summary>¿Hay credenciales de Cloudinary cargadas? Si no, se muestra el aviso de "no configurado".</summary>
    public bool StorageEnabled { get; set; }

    /// <summary>Con qué sombrero actúa el usuario (solo informativo en la interfaz).</summary>
    public ImageUploader Role { get; set; }

    public int MaxImages { get; set; } = 6;

    public long MaxFileSizeBytes { get; set; } = 5_242_880;

    public int MinDimensionPx { get; set; } = 800;

    /// <summary>Formatos aceptados, en minúscula: "jpg", "png", "webp"…</summary>
    public List<string> AllowedFormats { get; set; } = new();

    /// <summary>Valor del atributo <c>accept</c> del input de archivo.</summary>
    public string AcceptAttribute { get; set; } = "image/jpeg,image/png,image/webp";

    // ---------------------------------------------------------------- Derivados

    public int Count => Images.Count;

    public bool MaxImagesReached => Count >= MaxImages;

    public bool CanUploadMore => CanEdit && StorageEnabled && !MaxImagesReached;

    public double MaxFileSizeMb => Math.Round(MaxFileSizeBytes / 1024d / 1024d, 1);

    /// <summary>"JPG, PNG o WebP" para el texto de ayuda.</summary>
    public string FormatsLabel
    {
        get
        {
            var labels = AllowedFormats
                .Select(f => f == "jpeg" ? "JPG" : f.ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (labels.Count == 0)
            {
                return "JPG, PNG o WebP";
            }

            if (labels.Count == 1)
            {
                return labels[0];
            }

            return string.Join(", ", labels.Take(labels.Count - 1)) + " o " + labels[^1];
        }
    }
}

/// <summary>Una tarjeta del gestor. Se serializa a JSON en las respuestas AJAX.</summary>
public class ProductImageItemViewModel
{
    public Guid Id { get; set; }

    /// <summary>URL en variante Admin (240×240): la que se ve en el gestor.</summary>
    public string? ThumbUrl { get; set; }

    /// <summary>URL en variante Main: para abrir la foto en grande en una pestaña nueva.</summary>
    public string? FullUrl { get; set; }

    public bool IsCover { get; set; }

    public int SortOrder { get; set; }

    public string? AltText { get; set; }

    /// <summary>"Proveedor" | "Bean" — de dónde salió la foto.</summary>
    public string UploadedByLabel { get; set; } = string.Empty;

    public string UploadedOn { get; set; } = string.Empty;

    /// <summary>"1600 × 1600 · 820 KB" para el pie de la tarjeta.</summary>
    public string? Dimensions { get; set; }
}

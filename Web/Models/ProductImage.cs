using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Web.Models.Enums;

namespace Web.Models;

/// <summary>
/// Una foto de un lote de café. Relación 1:N con <see cref="Product"/>.
///
/// El archivo NO vive en nuestro servidor: el navegador lo sube directamente a
/// Cloudinary con una firma que emite nuestro backend, y aquí solo se guarda el
/// PUNTERO (<see cref="PublicId"/> + <see cref="SecureUrl"/>) más los metadatos que
/// devuelve Cloudinary (versión, formato, dimensiones, peso).
///
/// ⚠️ <see cref="Product.ImageUrl"/> NO se elimina: pasa a ser una caché
/// denormalizada de la URL de la PORTADA, ya transformada para tamaño de tarjeta.
/// Eso mantiene intactas las 6 vistas que ya la consumen (catálogo, carrito,
/// checkout, confirmación, mis pedidos, orígenes) sin meter Include(p =&gt; p.Images)
/// en el camino del dinero.
/// </summary>
public class ProductImage
{
    [Key]
    public Guid ProductImageID { get; set; }

    [Required]
    public Guid ProductID { get; set; }

    [ForeignKey("ProductID")]
    public Product? Product { get; set; }

    /// <summary>
    /// Identificador del recurso en Cloudinary, con su ruta completa:
    /// <c>bean/products/{ProductID:N}/{Guid:N}</c>. Lo elige SIEMPRE el servidor y va
    /// dentro de la firma: es lo que impide que el navegador suba a la carpeta de otro
    /// producto o sobrescriba un recurso existente. Es la llave para borrar (destroy).
    /// </summary>
    [Required]
    [StringLength(255)]
    public string PublicId { get; set; } = string.Empty;

    /// <summary>
    /// Versión que devuelve Cloudinary (<c>v1690000000</c>). Hace la URL INMUTABLE, de
    /// modo que el CDN y el navegador la pueden cachear indefinidamente.
    /// </summary>
    public long? Version { get; set; }

    /// <summary>Formato real detectado por Cloudinary: "jpg" | "png" | "webp".</summary>
    [StringLength(10)]
    public string? Format { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public long? Bytes { get; set; }

    /// <summary>
    /// <c>secure_url</c> tal como lo devolvió Cloudinary (https, sin transformaciones).
    /// Es la BASE desde la que se derivan las variantes por URL sin necesitar las
    /// credenciales en tiempo de render: ver <c>Web.Services.Media.ProductImageUrls</c>.
    /// </summary>
    [StringLength(500)]
    public string? SecureUrl { get; set; }

    /// <summary>Por si algún día se migra de proveedor de almacenamiento.</summary>
    [StringLength(30)]
    public string StorageProvider { get; set; } = "Cloudinary";

    /// <summary>Orden en la galería: compacto (0..n-1) tras cualquier borrado o reordenamiento.</summary>
    public int SortOrder { get; set; }

    /// <summary>Exactamente UNA true por producto mientras haya al menos una imagen activa.</summary>
    public bool IsCover { get; set; }

    [StringLength(200)]
    public string? AltText { get; set; }

    /// <summary>Proveedor o staff de Bean (auditoría del modelo de consignación).</summary>
    public ImageUploader UploadedByRole { get; set; }

    /// <summary>Usuario que la subió (mismo patrón que <see cref="Product.CreatedBy"/>).</summary>
    [Required]
    public string UploadedBy { get; set; } = string.Empty;

    public DateTime UploadedOn { get; set; }

    /// <summary>Soft delete, coherente con Product/Provider.</summary>
    public bool Status { get; set; } = true;
}

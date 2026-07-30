using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Web.Models.Enums;

namespace Web.Models;

public class Product
{
    [Key]
    public Guid ProductID { get; set; }

    [Required]
    public Guid ProviderID { get; set; }

    [ForeignKey("ProviderID")]
    public Provider? Provider { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Price { get; set; }

    [Required]
    public int Stock { get; set; }

    public string? ImageUrl { get; set; }

    // Workflow Status
    public ProductStatus ProductStatus { get; set; } = ProductStatus.Draft;

    public string? ShippingDetails { get; set; } // Tracking number, courier, etc.

    public string? RejectionReason { get; set; }

    // --- Atributos de café de especialidad (todos nullable: migración aditiva y segura) ---
    [StringLength(100)]
    public string? Origin { get; set; } // Región/origen, ej. "Huila"

    [StringLength(100)]
    public string? Farm { get; set; } // Finca, ej. "Finca La Victoria"

    [StringLength(50)]
    public string? Altitude { get; set; } // ej. "1.750 msnm"

    public CoffeeProcess? Process { get; set; } // Lavado / Honey / Natural

    [StringLength(100)]
    public string? Variety { get; set; } // ej. "Geisha"

    [StringLength(50)]
    public string? Lot { get; set; } // ej. "SNG-2026-04"

    public DateTime? RoastDate { get; set; } // Fecha de tueste

    [StringLength(300)]
    public string? TastingNotes { get; set; } // Separadas por coma, ej. "Jazmín,Durazno,Bergamota"

    [Column(TypeName = "decimal(3,2)")]
    public decimal? Rating { get; set; } // ej. 4.9

    public int? ReviewCount { get; set; } // Nº de reseñas

    // --- Empaque y envío (nullable: el catálogo viejo sigue funcionando con los
    //     valores por defecto de Shipping:Defaults) ---

    /// <summary>Peso de la unidad empacada, en gramos. Si falta se asume el default de configuración (500 g).</summary>
    public int? ShippingWeightGrams { get; set; }

    [Column(TypeName = "decimal(6,2)")]
    public decimal? LengthCm { get; set; }

    [Column(TypeName = "decimal(6,2)")]
    public decimal? WidthCm { get; set; }

    [Column(TypeName = "decimal(6,2)")]
    public decimal? HeightCm { get; set; }

    // Audit
    public bool Status { get; set; } = true; // Soft delete

    [Required]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedOn { get; set; }

    public string? UpdatedBy { get; set; }

    public DateTime? UpdatedOn { get; set; }

    // Navigation
    public ICollection<ProductCountry> ProductCountries { get; set; } = new List<ProductCountry>();

    /// <summary>
    /// Galería de fotos del lote (Cloudinary). NO se debe cargar con Include en el
    /// catálogo, el carrito ni los pedidos: esas vistas usan <see cref="ImageUrl"/>,
    /// que es la portada denormalizada. Solo se incluye donde se muestra la galería
    /// completa (Home/Details) o el gestor de fotos.
    /// </summary>
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
}

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

    /// <summary>
    /// PVP: lo que paga el cliente. Desde el ciclo BETA (ago-2026) lo fija BEAN, no el
    /// proveedor: solo lo edita quien tenga <c>Pricing/Update</c>. El catálogo, el carrito
    /// y el checkout siguen leyendo este campo, así que su significado hacia afuera no cambia.
    /// </summary>
    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Price { get; set; }

    /// <summary>
    /// COSTO: lo que Bean le paga al proveedor por unidad. Lo edita el proveedor en sus
    /// formularios. Margen = (Price − SupplierPrice) / Price.
    /// ⚠️ Solo visible con permiso <c>Pricing/Read</c>: nunca se expone en el storefront.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal SupplierPrice { get; set; }

    /// <summary>Cuándo se fijó el PVP por última vez (null = nunca se ha fijado).</summary>
    public DateTime? PriceSetAt { get; set; }

    /// <summary>Quién fijó el PVP por última vez.</summary>
    [StringLength(256)]
    public string? PriceSetBy { get; set; }

    /// <summary>
    /// true cuando el margen vigente quedó por debajo del mínimo configurado (típicamente
    /// porque el proveedor subió su costo). Alimenta la bandeja "Márgenes por revisar".
    /// NO bloquea la venta: el producto sigue en catálogo.
    /// </summary>
    public bool MarginAlert { get; set; }

    /// <summary>
    /// Total denormalizado de unidades disponibles entre TODAS las bodegas. La verdad
    /// contable es el libro de <see cref="StockMovement"/>; este campo lo recalcula
    /// <c>InventoryService</c> en la misma transacción. Lo consumen catálogo, carrito,
    /// checkout, confirmación, "Mis pedidos" y aprobaciones: no borrar.
    /// </summary>
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

    /// <summary>
    /// Histórico de cambios de costo y PVP. Solo se carga en las pantallas de pricing
    /// (permiso <c>Pricing/Read</c>); nunca en el catálogo ni en el carrito.
    /// </summary>
    public ICollection<PriceChangeLog> PriceChanges { get; set; } = new List<PriceChangeLog>();

    /// <summary>Saldos por bodega. La verdad contable es el libro de <see cref="StockMovement"/>.</summary>
    public ICollection<StockItem> StockItems { get; set; } = new List<StockItem>();
}

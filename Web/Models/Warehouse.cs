using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Web.Models;

/// <summary>
/// Bodega propia de Bean. Hoy hay una (Cali, <c>CAL-01</c>) pero el modelo es
/// multi-bodega y multi-país DESDE EL INICIO: por eso lleva <see cref="CountryID"/> y
/// <see cref="DaneCode"/> (para cotizar el envío DESDE esta bodega en vez de la constante
/// de <c>appsettings</c>).
/// </summary>
public class Warehouse
{
    /// <summary>
    /// Id fijo de la bodega inicial <c>CAL-01</c>. Lo comparten la migración
    /// <c>AddWarehouseInventory</c> (que la siembra en bases ya existentes) y
    /// <c>ContextSeed</c> (red de seguridad para bases nuevas): tienen que apuntar al
    /// MISMO registro o el backfill quedaría colgando de una bodega fantasma.
    /// </summary>
    public static readonly Guid DefaultWarehouseId = new("CA1D0001-0000-4000-8000-BEA000000001");

    /// <summary>Código de la bodega inicial.</summary>
    public const string DefaultWarehouseCode = "CAL-01";

    [Key]
    public Guid WarehouseID { get; set; }

    /// <summary>Código corto y ÚNICO, ej. <c>CAL-01</c>. Es lo que se ve en los listados.</summary>
    [Required]
    [StringLength(20)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Guid CountryID { get; set; }

    [ForeignKey("CountryID")]
    public virtual Country? Country { get; set; }

    [Required]
    [StringLength(100)]
    public string City { get; set; } = string.Empty;

    /// <summary>
    /// Código DANE del municipio (DIVIPOLA, 5 dígitos), enlaza con <see cref="ShippingCity"/>.
    /// ⚠️ Cali es <c>76001</c>. <c>760001</c> es el código POSTAL, no el DANE: no confundirlos.
    /// Nullable porque una bodega fuera de Colombia no tiene DANE; en ese caso el cotizador
    /// cae al origen configurado en <c>appsettings</c>.
    /// </summary>
    [StringLength(10)]
    public string? DaneCode { get; set; }

    [StringLength(200)]
    public string Address { get; set; } = string.Empty;

    /// <summary>Bodega por defecto de su país. Debe haber exactamente una por país.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Operativa. Una bodega con stock &gt; 0 no se puede desactivar.</summary>
    public bool IsActive { get; set; } = true;

    // Audit (patrón del proyecto)
    public bool Status { get; set; } = true;

    [Required]
    public string CreatedBy { get; set; } = "SYSTEM";

    public DateTime CreatedOn { get; set; }

    public string? ModifiedBy { get; set; }

    public DateTime? ModifiedOn { get; set; }

    // Navigation
    public ICollection<StockItem> StockItems { get; set; } = new List<StockItem>();
}

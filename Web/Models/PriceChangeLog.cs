using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Web.Models.Enums;

namespace Web.Models;

/// <summary>
/// Histórico INMUTABLE de cambios de precio de un lote. Nunca se edita ni se borra:
/// es la trazabilidad de quién movió el costo o el PVP, cuándo y con qué margen.
/// Solo visible con permiso <c>Pricing/Read</c>.
/// </summary>
public class PriceChangeLog
{
    [Key]
    public Guid PriceChangeLogID { get; set; }

    [Required]
    public Guid ProductID { get; set; }

    [ForeignKey("ProductID")]
    public virtual Product? Product { get; set; }

    /// <summary>Costo del proveedor o PVP de Bean.</summary>
    public PriceChangeType ChangeType { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal OldValue { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal NewValue { get; set; }

    /// <summary>Margen % ANTES del cambio (calculado con los dos precios vigentes entonces).</summary>
    [Column(TypeName = "decimal(9,4)")]
    public decimal MarginBefore { get; set; }

    /// <summary>Margen % DESPUÉS del cambio.</summary>
    [Column(TypeName = "decimal(9,4)")]
    public decimal MarginAfter { get; set; }

    [Required]
    [StringLength(256)]
    public string ChangedBy { get; set; } = string.Empty;

    public DateTime ChangedOn { get; set; }

    /// <summary>
    /// Motivo. OBLIGATORIO cuando se guarda un PVP por debajo del margen mínimo
    /// (promoción, liquidación); opcional en el resto de casos.
    /// </summary>
    [StringLength(300)]
    public string? Reason { get; set; }

    /// <summary>true cuando ya se le informó el cambio al proveedor (épica E4).</summary>
    public bool NotifiedProvider { get; set; }
}

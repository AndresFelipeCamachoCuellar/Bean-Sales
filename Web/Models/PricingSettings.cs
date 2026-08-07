using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Web.Models;

/// <summary>
/// Configuración de márgenes de Bean. Es una entidad de FILA ÚNICA: siempre existe
/// exactamente un registro, sembrado con el <see cref="SingletonId"/> conocido.
/// Se lee mucho y se escribe casi nunca, por eso se cachea en
/// <c>Web.Services.Pricing.PricingSettingsProvider</c>.
///
/// Valores iniciales decididos por el PO (07-ago-2026): objetivo 30 %, mínimo 15 %,
/// redondeo a 50 COP. El 15 % es el PISO que dispara la alerta, NO la meta: con 15 %
/// y la tarifa de Wompi (2,65 % + $700 + IVA) el neto real cae a ~10 %.
/// </summary>
public class PricingSettings
{
    /// <summary>
    /// Identificador fijo de la única fila. Vive aquí (y no en la migración a secas) para
    /// que el seed defensivo y el proveedor de configuración apunten al mismo registro.
    /// </summary>
    public static readonly Guid SingletonId = new("9E1C4B2A-0000-4000-8000-BEA000000001");

    [Key]
    public Guid PricingSettingsID { get; set; } = SingletonId;

    /// <summary>% de margen objetivo. Alimenta el PRECIO SUGERIDO. Ej. 30 = 30 %.</summary>
    [Column(TypeName = "decimal(5,2)")]
    [Range(0, 99.99, ErrorMessage = "El margen objetivo debe estar entre 0 y 99,99 %")]
    public decimal TargetMarginPercent { get; set; } = 30m;

    /// <summary>% de margen mínimo. Umbral de la ALERTA (no bloquea la venta). Ej. 15 = 15 %.</summary>
    [Column(TypeName = "decimal(5,2)")]
    [Range(0, 99.99, ErrorMessage = "El margen mínimo debe estar entre 0 y 99,99 %")]
    public decimal MinimumMarginPercent { get; set; } = 15m;

    /// <summary>Múltiplo al que se redondea HACIA ARRIBA el precio sugerido (COP).</summary>
    [Column(TypeName = "decimal(18,2)")]
    [Range(1, 100000, ErrorMessage = "El redondeo debe estar entre 1 y 100.000")]
    public decimal RoundingStep { get; set; } = 50m;

    // Audit (patrón del proyecto)
    public bool Status { get; set; } = true;

    [Required]
    public string CreatedBy { get; set; } = "SYSTEM";

    public DateTime CreatedOn { get; set; }

    public string? ModifiedBy { get; set; }

    public DateTime? ModifiedOn { get; set; }
}

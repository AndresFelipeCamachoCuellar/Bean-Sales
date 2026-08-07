using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Web.Models.Enums;

namespace Web.Models;

/// <summary>
/// Libro mayor de inventario. INMUTABLE: nunca se edita ni se borra una fila; un error se
/// corrige con un asiento de signo contrario (<see cref="StockMovementType.Adjustment"/>).
///
/// Invariante del sistema:
/// <c>SUM(StockMovement.Quantity) == StockItem.QuantityOnHand</c> para toda combinación
/// producto × bodega.
/// </summary>
public class StockMovement
{
    [Key]
    public Guid StockMovementID { get; set; }

    [Required]
    public Guid ProductID { get; set; }

    [ForeignKey("ProductID")]
    public virtual Product? Product { get; set; }

    [Required]
    public Guid WarehouseID { get; set; }

    [ForeignKey("WarehouseID")]
    public virtual Warehouse? Warehouse { get; set; }

    public StockMovementType MovementType { get; set; }

    /// <summary>
    /// Cantidad FIRMADA: positiva si entra a la bodega, negativa si sale.
    /// Nunca 0 (<c>InventoryService</c> lo rechaza).
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>Qué originó el movimiento: <c>Order</c>, <c>Reception</c>, <c>Transfer</c>, <c>Settlement</c>…</summary>
    [StringLength(30)]
    public string? ReferenceType { get; set; }

    /// <summary>Id del documento de origen (p. ej. el <c>OrderID</c>).</summary>
    public Guid? ReferenceID { get; set; }

    /// <summary>
    /// Motivo. OBLIGATORIO en <see cref="StockMovementType.Adjustment"/> y
    /// <see cref="StockMovementType.Loss"/> (lo exige <c>InventoryService</c>).
    /// </summary>
    [StringLength(300)]
    public string? Reason { get; set; }

    /// <summary>Costo del proveedor en el momento del movimiento. Alimenta la liquidación de E3.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? UnitCost { get; set; }

    [Required]
    [StringLength(256)]
    public string CreatedBy { get; set; } = "SYSTEM";

    public DateTime CreatedOn { get; set; }
}

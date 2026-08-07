using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Web.Models.Enums;

namespace Web.Models;

/// <summary>
/// Saldo de un producto en una bodega. Clave compuesta <c>(ProductID, WarehouseID)</c>.
///
/// Es un DENORMALIZADO: la verdad contable es la suma del libro de
/// <see cref="StockMovement"/>. Se mantienen en la MISMA transacción desde
/// <c>InventoryService.MoveStockAsync</c>, que es el único punto del código autorizado a
/// escribir stock.
/// </summary>
public class StockItem
{
    public Guid ProductID { get; set; }

    [ForeignKey("ProductID")]
    public virtual Product? Product { get; set; }

    public Guid WarehouseID { get; set; }

    [ForeignKey("WarehouseID")]
    public virtual Warehouse? Warehouse { get; set; }

    /// <summary>Unidades FÍSICAS en la bodega (incluye las reservadas, que todavía no salieron).</summary>
    public int QuantityOnHand { get; set; }

    /// <summary>
    /// Unidades comprometidas por pedidos en <c>Pending</c> de pago. Salen de
    /// <see cref="QuantityAvailable"/> pero siguen contando en <see cref="QuantityOnHand"/>
    /// hasta que el pedido se despacha (o se libera la reserva).
    /// </summary>
    public int QuantityReserved { get; set; }

    /// <summary>
    /// Lo que realmente se puede vender. CALCULADO, <b>nunca</b> persistido: si se
    /// guardara, cualquier escritura parcial lo dejaría mintiendo.
    /// </summary>
    [NotMapped]
    public int QuantityAvailable => QuantityOnHand - QuantityReserved;

    /// <summary>De quién es este café (se congela en la recepción según el acuerdo vigente).</summary>
    public StockOwnership Ownership { get; set; } = StockOwnership.BeanOwned;

    /// <summary>Umbral de reposición. Si es null no se alerta por stock bajo.</summary>
    public int? ReorderPoint { get; set; }

    /// <summary>
    /// Token de concurrencia optimista. Cierra la deuda ya anotada del proyecto: dos
    /// cancelaciones simultáneas del mismo pedido NO pueden reintegrar dos veces.
    /// </summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    // Audit (patrón del proyecto)
    public bool Status { get; set; } = true;

    [Required]
    public string CreatedBy { get; set; } = "SYSTEM";

    public DateTime CreatedOn { get; set; }

    public string? ModifiedBy { get; set; }

    public DateTime? ModifiedOn { get; set; }
}

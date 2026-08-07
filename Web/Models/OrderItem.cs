using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Web.Models;

public class OrderItem
{
    [Key]
    public Guid OrderItemID { get; set; }

    [Required]
    public Guid OrderID { get; set; }

    [ForeignKey("OrderID")]
    public virtual Order? Order { get; set; }

    [Required]
    public Guid ProductID { get; set; }

    [ForeignKey("ProductID")]
    public virtual Product? Product { get; set; }

    // Snapshot del nombre del producto al momento de la compra.
    [Required]
    public string ProductName { get; set; } = string.Empty;

    // Snapshot del precio unitario (PVP) al momento de la compra.
    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Snapshot del COSTO del proveedor al momento de la compra. Se toma igual que
    /// <see cref="UnitPrice"/> y es IMPRESCINDIBLE para liquidar al proveedor (épica E3):
    /// cambiar el costo del producto después no puede alterar lo que se le debe por una
    /// venta ya ocurrida.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal SupplierPriceSnapshot { get; set; }

    public int Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal SubTotal { get; set; }

    /// <summary>
    /// Bodega de la que salió esta línea. Nullable: los pedidos anteriores al inventario
    /// multi-bodega no la tienen. Sin FK a propósito — es un dato de TRAZA histórica y no
    /// debe impedir desactivar o reorganizar bodegas años después.
    /// </summary>
    public Guid? WarehouseID { get; set; }
}

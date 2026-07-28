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

    // Snapshot del precio unitario al momento de la compra.
    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal SubTotal { get; set; }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Web.Models.Enums;

namespace Web.Models;

public class Order
{
    [Key]
    public Guid OrderID { get; set; }

    // Cliente que realizó la compra (misma clave que ApplicationUser: Guid).
    [Required]
    public Guid UserID { get; set; }

    [ForeignKey("UserID")]
    public virtual ApplicationUser? User { get; set; }

    public DateTime OrderDate { get; set; }

    // Estado / hito de seguimiento.
    public OrderStatus OrderStatus { get; set; } = OrderStatus.Confirmed;

    // Datos de envío como STRINGS (la vista postea el país como texto libre;
    // no se usa FK a Country para no romper ese flujo).
    [Required]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    public string LastName { get; set; } = string.Empty;

    [Required]
    public string Address { get; set; } = string.Empty;

    public string? State { get; set; }

    public string? ZipCode { get; set; }

    [Required]
    public string ShippingCountry { get; set; } = string.Empty;

    // Método de pago (placeholder: credit / paypal / pse). Aún sin pasarela real.
    [Required]
    public string PaymentMethod { get; set; } = string.Empty;

    [Required]
    public string CurrencyCode { get; set; } = "COP";

    [Column(TypeName = "decimal(18,2)")]
    public decimal Subtotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ShippingCost { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalAmount { get; set; }

    // Navigation
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}

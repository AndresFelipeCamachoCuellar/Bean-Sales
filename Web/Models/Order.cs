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

    /// <summary>
    /// Celular de quien recibe el pedido. Nullable porque los pedidos anteriores a este
    /// campo no lo tienen, pero el checkout lo exige: Wompi obliga a enviar
    /// <c>shipping-address:phone-number</c> siempre que se envíe el bloque de dirección
    /// (y la transportadora lo necesita para coordinar la entrega).
    /// </summary>
    [StringLength(30)]
    public string? CustomerPhone { get; set; }

    [Required]
    public string ShippingCountry { get; set; } = string.Empty;

    // Método de pago elegido en NUESTRO sitio. Con Web Checkout de Wompi el cliente
    // elige el método REAL dentro de la pasarela, así que aquí se guarda "wompi" y el
    // método efectivo queda en PaymentMethodUsed. Los pedidos históricos conservan
    // sus valores antiguos (credit / paypal / pse): la columna no se toca.
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

    // --- Envío dinámico (nullable: los pedidos anteriores a la tarifa dinámica no lo tienen) ---

    /// <summary>Municipio de destino (nombre legible, snapshot al momento de comprar).</summary>
    [StringLength(120)]
    public string? ShippingCity { get; set; }

    /// <summary>Código DANE del municipio de destino (DIVIPOLA).</summary>
    [StringLength(10)]
    public string? ShippingCityDaneCode { get; set; }

    /// <summary>Transportadora cotizada (nombre mostrado al cliente).</summary>
    [StringLength(80)]
    public string? ShippingCarrier { get; set; }

    /// <summary>Días hábiles estimados de entrega según la cotización.</summary>
    public int? ShippingEstimatedDays { get; set; }

    /// <summary>Origen de la tarifa: "Fixed" | "Mipaquete" | "Fallback".</summary>
    [StringLength(20)]
    public string? ShippingQuoteSource { get; set; }

    // --- Pago (Wompi) -------------------------------------------------------
    // Dimensión ORTOGONAL a OrderStatus: aquél es la logística, esto es el dinero.
    // ⚠️ Los pedidos anteriores a esta migración quedan con PaymentStatus = Pending (0);
    //    la migración debe hacer el backfill a Approved (ver instrucciones del incremento).

    /// <summary>Estado del pago. Solo <c>Approved</c> cuenta como venta real.</summary>
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;

    /// <summary>
    /// Referencia única enviada a Wompi en el intento VIGENTE.
    /// Formato: <c>BEAN-{OrderID:N}-{intento}</c>, así cualquier referencia emitida se
    /// puede resolver de vuelta a su pedido aunque el intento ya no sea el actual.
    /// </summary>
    [StringLength(64)]
    public string? PaymentReference { get; set; }

    /// <summary>ID de la transacción en Wompi del intento vigente (ej. "01-1531231271-19365").</summary>
    [StringLength(64)]
    public string? PaymentTransactionId { get; set; }

    /// <summary>Método REAL usado en la pasarela: CARD, PSE, NEQUI, BANCOLOMBIA_TRANSFER…</summary>
    [StringLength(40)]
    public string? PaymentMethodUsed { get; set; }

    /// <summary>Mensaje del procesador cuando el pago no prospera ("Fondos insuficientes").</summary>
    [StringLength(255)]
    public string? PaymentStatusMessage { get; set; }

    /// <summary>Ambiente de la transacción: "test" | "prod". Evita mezclar sandbox y producción.</summary>
    [StringLength(10)]
    public string? PaymentEnvironment { get; set; }

    /// <summary>
    /// Monto EXACTO en centavos que se firmó y se envió a la pasarela.
    /// Es el valor contra el que se verifica el <c>amount_in_cents</c> del webhook.
    /// </summary>
    public long? PaymentAmountInCents { get; set; }

    /// <summary>Momento de la aprobación (null mientras no esté pagado).</summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>
    /// Vencimiento de la reserva de stock. También viaja a Wompi como <c>expiration-time</c>,
    /// para que la pasarela muestre el contador y expire la transacción a la vez que nosotros.
    /// </summary>
    public DateTime? PaymentExpiresAt { get; set; }

    /// <summary>Nº de intentos de pago. Cada reintento genera una referencia nueva.</summary>
    public int PaymentAttempt { get; set; }

    // Navigation
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}

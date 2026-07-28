namespace Web.Models.Enums;

// Hitos de seguimiento del pedido (mapean a la línea de tiempo del handoff:
// Confirmado / Tostado y empacado / En camino / Entregado).
public enum OrderStatus
{
    Pending = 0,
    Confirmed = 1,
    Processing = 2,
    Shipped = 3,
    Delivered = 4,
    Cancelled = 5
}

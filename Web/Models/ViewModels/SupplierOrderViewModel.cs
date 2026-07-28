using Web.Models;

namespace Web.Models.ViewModels;

// Fila de "Pedidos entrantes" del proveedor (handoff vista 13).
// Envuelve un Order y agrega SOLO la información relevante para el proveedor
// actual: sus líneas del pedido y "Tu pago" (suma de subtotales de esas líneas,
// NO el total del pedido, que puede incluir productos de otros proveedores).
public class SupplierOrderViewModel
{
    public Order Order { get; set; } = null!;

    // Líneas del pedido que pertenecen al proveedor actual.
    public List<OrderItem> ProviderItems { get; set; } = new();

    // "Tu pago": suma de SubTotal de las líneas del proveedor actual.
    public decimal Payout { get; set; }

    // Cola a la que pertenece el pedido para el filtro por pestañas:
    // "preparar" | "listos" | "completados".
    public string Group { get; set; } = "preparar";
}

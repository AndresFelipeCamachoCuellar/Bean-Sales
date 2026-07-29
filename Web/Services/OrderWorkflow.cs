using Web.Models.Enums;

namespace Web.Services;

/// <summary>
/// Máquina de estados del pedido (única fuente de verdad para el back-office).
/// Se mantiene estática y sin dependencias para poder usarse desde controlador y vistas.
/// </summary>
public static class OrderWorkflow
{
    /// <summary>Estados a los que se puede avanzar desde el estado actual.</summary>
    public static IReadOnlyList<OrderStatus> NextStates(OrderStatus current) => current switch
    {
        OrderStatus.Pending => new[] { OrderStatus.Confirmed },
        OrderStatus.Confirmed => new[] { OrderStatus.Processing },
        OrderStatus.Processing => new[] { OrderStatus.Shipped },
        OrderStatus.Shipped => new[] { OrderStatus.Delivered },
        _ => Array.Empty<OrderStatus>() // Delivered y Cancelled son terminales
    };

    /// <summary>
    /// ¿Se puede avanzar de estado desde el back-office?
    /// Un pedido en Pending está esperando el PAGO: confirmarlo a mano equivaldría a
    /// despachar café gratis. El resto de transiciones no dependen del pago.
    /// </summary>
    public static bool CanAdvance(OrderStatus current, PaymentStatus payment) =>
        current != OrderStatus.Pending || payment == PaymentStatus.Approved;

    /// <summary>Un pedido se puede cancelar salvo que ya esté entregado o cancelado.</summary>
    public static bool CanCancel(OrderStatus current) =>
        current != OrderStatus.Delivered && current != OrderStatus.Cancelled;

    /// <summary>
    /// ¿Al cancelar se devuelve el stock al inventario?
    /// Sí mientras el pedido no haya salido de bodega. Si ya está Shipped el ajuste es manual.
    /// </summary>
    public static bool ShouldRestock(OrderStatus current) =>
        current == OrderStatus.Pending
        || current == OrderStatus.Confirmed
        || current == OrderStatus.Processing;

    /// <summary>Texto largo del botón que lleva al estado indicado (vista de detalle).</summary>
    public static string ActionLabel(OrderStatus next) => next switch
    {
        OrderStatus.Confirmed => "Confirmar pedido",
        OrderStatus.Processing => "Marcar en preparación",
        OrderStatus.Shipped => "Marcar como enviado",
        OrderStatus.Delivered => "Marcar como entregado",
        _ => "Actualizar estado"
    };

    /// <summary>Texto corto del botón inline del listado.</summary>
    public static string ShortActionLabel(OrderStatus next) => next switch
    {
        OrderStatus.Confirmed => "Confirmar",
        OrderStatus.Processing => "Preparar",
        OrderStatus.Shipped => "Despachar",
        OrderStatus.Delivered => "Entregar",
        _ => "Avanzar"
    };
}

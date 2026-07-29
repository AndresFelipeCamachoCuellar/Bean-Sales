namespace Web.Models.Enums;

/// <summary>
/// Estado del PAGO del pedido. Es una dimensión ORTOGONAL a <see cref="OrderStatus"/>:
/// aquél es el seguimiento logístico (Confirmado / En preparación / Enviado…), éste es
/// el dinero. Mantenerlos separados evita tocar los switches de las 6 vistas que ya
/// consumen OrderStatus.
///
/// Mapeo con los estados de transacción de Wompi:
///   APPROVED -> Approved · DECLINED -> Declined · VOIDED -> Voided · ERROR -> Error
///   PENDING  -> Pending (no cambia nada)
/// <c>Expired</c> es interno: el pedido venció sin pagarse y se liberó la reserva de stock.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Pedido creado, stock reservado, esperando que el cliente pague.</summary>
    Pending = 0,

    /// <summary>Pago aprobado. Es el ÚNICO estado que cuenta como venta real.</summary>
    Approved = 1,

    /// <summary>Rechazado por el banco/emisor. El cliente puede reintentar (referencia nueva).</summary>
    Declined = 2,

    /// <summary>Anulada (solo tarjeta). Se cancela el pedido y se reintegra el stock.</summary>
    Voided = 3,

    /// <summary>Error de procesamiento. El cliente puede reintentar.</summary>
    Error = 4,

    /// <summary>Interno: venció el plazo de pago; el pedido se canceló y el stock volvió.</summary>
    Expired = 5
}

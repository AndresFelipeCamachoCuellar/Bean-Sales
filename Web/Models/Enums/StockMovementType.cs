namespace Web.Models.Enums;

/// <summary>
/// Tipos de asiento del libro mayor de inventario (<see cref="Web.Models.StockMovement"/>).
/// El signo NO va aquí: lo lleva <c>StockMovement.Quantity</c> (+ entra, − sale). Este enum
/// solo dice POR QUÉ se movió.
/// </summary>
public enum StockMovementType
{
    /// <summary>Entrada: llegó un lote del proveedor a la bodega.</summary>
    Reception = 0,

    /// <summary>Salida: se vendió y se despachó.</summary>
    Sale = 1,

    /// <summary>Entrada: se canceló una venta y el lote vuelve a estar disponible.</summary>
    SaleCancelled = 2,

    /// <summary>Entrada: el cliente devolvió el producto y volvió a bodega.</summary>
    CustomerReturn = 3,

    /// <summary>Cuadre manual de conteo. Requiere razón OBLIGATORIA.</summary>
    Adjustment = 4,

    /// <summary>Salida: traslado hacia otra bodega (v1.1).</summary>
    TransferOut = 5,

    /// <summary>Entrada: traslado desde otra bodega (v1.1).</summary>
    TransferIn = 6,

    /// <summary>Salida: merma, daño o pérdida. Requiere razón OBLIGATORIA.</summary>
    Loss = 7,

    /// <summary>Salida: se le devolvió el lote al proveedor (consignación no vendida).</summary>
    ReturnToProvider = 8
}

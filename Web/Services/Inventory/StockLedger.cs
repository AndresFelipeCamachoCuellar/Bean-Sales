using Web.Models.Enums;

namespace Web.Services.Inventory;

/// <summary>Saldo de una combinación producto × bodega.</summary>
public readonly record struct StockBalance(int OnHand, int Reserved)
{
    /// <summary>Lo que realmente se puede vender.</summary>
    public int Available => OnHand - Reserved;
}

/// <summary>
/// Núcleo de cálculo del inventario. Clase PURA: sin EF, sin HTTP, sin estado — mismo
/// patrón que <c>ShippingPackageBuilder</c>, <c>WompiSignature</c> y <c>PricingCalculator</c>.
///
/// <see cref="InventoryService"/> es la cáscara de datos (transacción, RowVersion,
/// recálculo del denormalizado); TODA la aritmética y las reglas de validación viven aquí
/// para poder probarlas con xUnit sin base de datos.
/// </summary>
public static class StockLedger
{
    /// <summary>Tipos de movimiento que exigen una razón escrita (auditoría de descuadres).</summary>
    public static bool RequiresReason(StockMovementType type) =>
        type is StockMovementType.Adjustment or StockMovementType.Loss;

    /// <summary>
    /// Valida la FORMA del movimiento antes de tocar la base de datos.
    /// </summary>
    /// <exception cref="InventoryException">Cantidad 0 o razón faltante donde es obligatoria.</exception>
    public static void ValidateMovement(StockMovementType type, int signedQuantity, string? reason)
    {
        if (signedQuantity == 0)
        {
            throw new InventoryException("Un movimiento de inventario no puede ser de 0 unidades.");
        }

        if (RequiresReason(type) && string.IsNullOrWhiteSpace(reason))
        {
            throw new InventoryException(
                $"El movimiento de tipo {type} exige una razón: es la única forma de auditar el descuadre.");
        }
    }

    /// <summary>
    /// Aplica un movimiento al saldo. <paramref name="signedQuantity"/> es positiva si entra
    /// a la bodega y negativa si sale.
    ///
    /// Regla clave: una SALIDA por venta CONSUME la reserva que se hizo al crear el pedido.
    /// Sin esto el disponible se descontaría dos veces (una al reservar y otra al vender).
    /// </summary>
    /// <exception cref="InventoryException">Si el resultado dejaría el inventario en negativo.</exception>
    public static StockBalance Apply(StockBalance current, StockMovementType type, int signedQuantity)
    {
        var onHand = current.OnHand + signedQuantity;

        if (onHand < 0)
        {
            throw new InventoryException(
                $"El movimiento dejaría el inventario en negativo ({onHand}). " +
                $"Hay {current.OnHand} unidades en esa bodega.");
        }

        var reserved = current.Reserved;

        if (type == StockMovementType.Sale && reserved > 0)
        {
            reserved = Math.Max(0, reserved - Math.Abs(signedQuantity));
        }

        return new StockBalance(onHand, reserved);
    }

    /// <summary>
    /// Aparta unidades para un pedido pendiente de pago. No mueve el físico: reduce el
    /// disponible.
    /// </summary>
    /// <exception cref="InventoryException">Cantidad no positiva o disponible insuficiente.</exception>
    public static StockBalance Reserve(StockBalance current, int quantity)
    {
        if (quantity <= 0)
        {
            throw new InventoryException("La reserva debe ser de al menos 1 unidad.");
        }

        if (current.Available < quantity)
        {
            throw new InventoryException(
                $"No hay unidades suficientes para reservar en esa bodega (disponibles: {current.Available}).");
        }

        return current with { Reserved = current.Reserved + quantity };
    }

    /// <summary>
    /// Libera una reserva. NUNCA deja <c>Reserved</c> negativo: si el dato ya venía
    /// descuadrado se corrige a 0 en vez de propagar el error (el llamador lo registra).
    /// </summary>
    public static StockBalance Release(StockBalance current, int quantity)
    {
        if (quantity <= 0) return current;

        var reserved = current.Reserved - quantity;
        if (reserved < 0) reserved = 0;

        return current with { Reserved = reserved };
    }

    /// <summary>
    /// Invariante del sistema: el saldo físico es exactamente la suma de las cantidades
    /// firmadas del libro mayor. Lo usan los tests y sirve como base de una futura
    /// pantalla de conciliación.
    /// </summary>
    public static int ReplayOnHand(IEnumerable<int> signedQuantities) =>
        (signedQuantities ?? Enumerable.Empty<int>()).Sum();

    /// <summary>
    /// Total denormalizado que se guarda en <c>Product.Stock</c>: el DISPONIBLE agregado de
    /// todas las bodegas, nunca negativo.
    /// </summary>
    public static int TotalAvailable(IEnumerable<StockBalance> balances)
    {
        var total = (balances ?? Enumerable.Empty<StockBalance>()).Sum(b => b.Available);
        return total < 0 ? 0 : total;
    }
}

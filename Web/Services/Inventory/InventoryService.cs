using Microsoft.EntityFrameworkCore;
using Web.Data;
using Web.Models;
using Web.Models.Enums;

namespace Web.Services.Inventory;

/// <summary>
/// Referencia al documento que originó un movimiento de stock (pedido, recepción, traslado…).
/// </summary>
public readonly record struct StockReference(string? Type, Guid? Id)
{
    public static StockReference None => new(null, null);

    public static StockReference Order(Guid orderId) => new("Order", orderId);

    public static StockReference Reception(Guid productId) => new("Reception", productId);
}

/// <summary>
/// Resultado de un movimiento aplicado.
/// </summary>
public sealed record StockMoveResult(
    Guid StockMovementID,
    Guid ProductID,
    Guid WarehouseID,
    int QuantityOnHand,
    int QuantityReserved,
    int ProductTotalStock);

/// <summary>Se intentó un movimiento imposible o mal formado.</summary>
public sealed class InventoryException : Exception
{
    public InventoryException(string message) : base(message) { }
}

/// <summary>
/// ÚNICO punto del código autorizado a escribir stock.
///
/// Cada operación, EN UNA SOLA TRANSACCIÓN:
///   1. escribe el asiento en <see cref="StockMovement"/> (libro mayor inmutable),
///   2. actualiza el saldo del <see cref="StockItem"/> (creándolo si no existe),
///   3. recalcula <c>Product.Stock</c> = SUM de <c>QuantityOnHand</c> de todas sus bodegas.
///
/// La concurrencia se resuelve con el <c>RowVersion</c> de <see cref="StockItem"/> y un
/// reintento acotado: dos cancelaciones simultáneas del mismo pedido no pueden reintegrar
/// dos veces.
/// </summary>
public sealed class InventoryService
{
    /// <summary>Reintentos ante choque de concurrencia optimista. Acotado a propósito.</summary>
    private const int MaxConcurrencyRetries = 3;

    private readonly ApplicationDbContext _context;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(ApplicationDbContext context, ILogger<InventoryService> logger)
    {
        _context = context;
        _logger = logger;
    }

    // ================================================================= Consultas

    /// <summary>
    /// Bodega que despacha por defecto. Preferencia: la marcada <c>IsDefault</c> y activa;
    /// si no hay, la primera activa. Devuelve null si todavía no hay ninguna bodega
    /// (degradación segura: el llamador decide si bloquea o sigue con el modo antiguo).
    /// </summary>
    public async Task<Warehouse?> GetDefaultWarehouseAsync(CancellationToken ct = default)
    {
        return await _context.Warehouses
            .Where(w => w.IsActive && w.Status)
            .OrderByDescending(w => w.IsDefault)
            .ThenBy(w => w.Code)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Saldo de un producto en una bodega (null si nunca ha tenido stock ahí).</summary>
    public async Task<StockItem?> FindStockItemAsync(Guid productId, Guid warehouseId, CancellationToken ct = default)
    {
        return await _context.StockItems
            .FirstOrDefaultAsync(s => s.ProductID == productId && s.WarehouseID == warehouseId, ct);
    }

    // ================================================================ Escritura

    /// <summary>
    /// Aplica un movimiento de stock. <paramref name="quantity"/> va FIRMADA
    /// (+ entra a la bodega, − sale). Abre su propia transacción salvo que ya haya una
    /// en curso, en cuyo caso se suma a la del llamador.
    /// </summary>
    /// <exception cref="InventoryException">
    /// Cantidad 0, razón faltante en <c>Adjustment</c>/<c>Loss</c>, bodega inexistente o
    /// saldo insuficiente.
    /// </exception>
    public async Task<StockMoveResult> MoveStockAsync(
        Guid productId,
        Guid warehouseId,
        StockMovementType movementType,
        int quantity,
        StockReference reference,
        string? reason,
        decimal? unitCost,
        string user,
        CancellationToken ct = default)
    {
        // --- Validaciones de forma (baratas y antes de tocar la BD). Viven en StockLedger:
        //     así se prueban sin base de datos.
        StockLedger.ValidateMovement(movementType, quantity, reason);

        var warehouseExists = await _context.Warehouses.AnyAsync(w => w.WarehouseID == warehouseId, ct);
        if (!warehouseExists)
        {
            throw new InventoryException("La bodega indicada no existe.");
        }

        // --- Reintento acotado ante choque de RowVersion.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ApplyMoveAsync(
                    productId, warehouseId, movementType, quantity, reference, reason, unitCost, user, ct);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyRetries)
            {
                _logger.LogWarning(
                    "Choque de concurrencia moviendo stock del producto {ProductId} en la bodega {WarehouseId} " +
                    "(intento {Attempt}/{Max}). Se reintenta con el saldo fresco.",
                    productId, warehouseId, attempt, MaxConcurrencyRetries);

                // Hay que soltar las entidades viejas o el reintento releería la MISMA
                // versión desde el change tracker y volvería a chocar indefinidamente.
                DetachStockEntities(productId, warehouseId);
            }
        }
    }

    /// <summary>
    /// Reserva unidades para un pedido pendiente de pago: no las saca de la bodega, las
    /// aparta. Mueve <c>QuantityReserved</c>, NO <c>QuantityOnHand</c>, así que no genera
    /// asiento en el libro mayor (todavía no hubo movimiento físico).
    /// </summary>
    public async Task ReserveAsync(
        Guid productId, Guid warehouseId, int quantity, string user, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var item = await LoadOrCreateStockItemAsync(productId, warehouseId, user, ct);

                var balance = StockLedger.Reserve(
                    new StockBalance(item.QuantityOnHand, item.QuantityReserved), quantity);

                item.QuantityReserved = balance.Reserved;
                Touch(item, user);

                await _context.SaveChangesAsync(ct);

                // Reservar reduce el DISPONIBLE, y Product.Stock es el disponible agregado:
                // sin este recálculo el catálogo seguiría ofreciendo unidades apartadas.
                await RecalculateProductStockAsync(productId, ct);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyRetries)
            {
                DetachStockEntities(productId, warehouseId);
            }
        }
    }

    /// <summary>
    /// Libera una reserva sin mover unidades físicas (pago vencido, cliente que desiste).
    /// Nunca deja <c>QuantityReserved</c> negativo: si el dato ya estaba descuadrado, se
    /// registra y se corrige a 0 en vez de propagar el error.
    /// </summary>
    public async Task ReleaseReservationAsync(
        Guid productId, Guid warehouseId, int quantity, string user, CancellationToken ct = default)
    {
        if (quantity <= 0) return;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var item = await FindStockItemAsync(productId, warehouseId, ct);
                if (item == null) return; // Nada que liberar.

                if (item.QuantityReserved < quantity)
                {
                    _logger.LogWarning(
                        "Se pidió liberar {Cantidad} unidades reservadas del producto {ProductId} en la bodega " +
                        "{WarehouseId}, pero solo había {Reservadas}. Se deja la reserva en 0.",
                        quantity, productId, warehouseId, item.QuantityReserved);
                }

                var balance = StockLedger.Release(
                    new StockBalance(item.QuantityOnHand, item.QuantityReserved), quantity);

                item.QuantityReserved = balance.Reserved;
                Touch(item, user);

                await _context.SaveChangesAsync(ct);

                // Liberar devuelve unidades al disponible.
                await RecalculateProductStockAsync(productId, ct);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyRetries)
            {
                DetachStockEntities(productId, warehouseId);
            }
        }
    }

    // ============================================================ Nivel pedido

    /// <summary>
    /// El pedido se pagó: la RESERVA se convierte en salida real de bodega
    /// (movimiento <see cref="StockMovementType.Sale"/>). <c>MoveStockAsync</c> consume la
    /// reserva al aplicar el <c>Sale</c>, así que el disponible no se descuenta dos veces.
    ///
    /// Idempotencia: si ya existe un <c>Sale</c> para este pedido no hace nada. Es
    /// imprescindible porque el pago puede confirmarse por el webhook Y por el retorno del
    /// cliente.
    /// </summary>
    public async Task ConfirmOrderSaleAsync(Order order, string user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        var orderId = order.OrderID;

        var yaRegistrado = await _context.StockMovements
            .AnyAsync(m => m.ReferenceType == "Order"
                           && m.ReferenceID == orderId
                           && m.MovementType == StockMovementType.Sale, ct);

        if (yaRegistrado) return;

        foreach (var line in order.Items)
        {
            if (line.WarehouseID is not Guid warehouseId) continue;

            await MoveStockAsync(
                line.ProductID, warehouseId, StockMovementType.Sale, -line.Quantity,
                StockReference.Order(orderId), reason: null,
                unitCost: line.SupplierPriceSnapshot, user: user, ct: ct);
        }
    }

    /// <summary>
    /// El pedido se cancela. Dos caminos, según si el café ya salió del disponible como
    /// VENTA o solo estaba APARTADO:
    ///  · <paramref name="onlyReserved"/> = true (pedido en <c>Pending</c>, nunca pagado):
    ///    se libera la reserva. No hay asiento porque no hubo movimiento físico.
    ///  · false (ya confirmado/en preparación): se registra <c>SaleCancelled</c> y las
    ///    unidades vuelven al disponible.
    ///
    /// Idempotente en ambos casos: no reintegra dos veces aunque entren dos cancelaciones
    /// simultáneas (la segunda encuentra el asiento ya escrito).
    /// </summary>
    public async Task ReleaseOrderStockAsync(
        Order order, bool onlyReserved, string user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        var orderId = order.OrderID;

        if (!onlyReserved)
        {
            var yaReintegrado = await _context.StockMovements
                .AnyAsync(m => m.ReferenceType == "Order"
                               && m.ReferenceID == orderId
                               && m.MovementType == StockMovementType.SaleCancelled, ct);

            if (yaReintegrado) return;
        }

        foreach (var line in order.Items)
        {
            if (line.WarehouseID is not Guid warehouseId) continue;

            if (onlyReserved)
            {
                await ReleaseReservationAsync(line.ProductID, warehouseId, line.Quantity, user, ct);
            }
            else
            {
                await MoveStockAsync(
                    line.ProductID, warehouseId, StockMovementType.SaleCancelled, line.Quantity,
                    StockReference.Order(orderId), reason: null,
                    unitCost: line.SupplierPriceSnapshot, user: user, ct: ct);
            }
        }
    }

    // ================================================================== Interno

    private async Task<StockMoveResult> ApplyMoveAsync(
        Guid productId,
        Guid warehouseId,
        StockMovementType movementType,
        int quantity,
        StockReference reference,
        string? reason,
        decimal? unitCost,
        string user,
        CancellationToken ct)
    {
        // Si el llamador ya abrió transacción (el checkout lo hace), se respeta la suya:
        // abrir una anidada con SQL Server lanzaría.
        var ownsTransaction = _context.Database.CurrentTransaction == null;
        var transaction = ownsTransaction
            ? await _context.Database.BeginTransactionAsync(ct)
            : null;

        try
        {
            var item = await LoadOrCreateStockItemAsync(productId, warehouseId, user, ct);

            // Toda la aritmética (y el rechazo del inventario negativo) vive en el núcleo puro.
            var balance = StockLedger.Apply(
                new StockBalance(item.QuantityOnHand, item.QuantityReserved), movementType, quantity);

            item.QuantityOnHand = balance.OnHand;
            item.QuantityReserved = balance.Reserved;

            Touch(item, user);

            // 1. Asiento del libro mayor.
            var movement = new StockMovement
            {
                StockMovementID = Guid.NewGuid(),
                ProductID = productId,
                WarehouseID = warehouseId,
                MovementType = movementType,
                Quantity = quantity,
                ReferenceType = reference.Type,
                ReferenceID = reference.Id,
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                UnitCost = unitCost,
                CreatedBy = string.IsNullOrWhiteSpace(user) ? "SYSTEM" : user,
                CreatedOn = DateTime.Now
            };

            _context.StockMovements.Add(movement);

            // 2. Guardar saldo + asiento juntos (aquí es donde salta el RowVersion si otro
            //    proceso tocó el mismo StockItem).
            await _context.SaveChangesAsync(ct);

            // 3. Recalcular el total denormalizado que consumen catálogo, carrito y checkout.
            var totalStock = await RecalculateProductStockAsync(productId, ct);

            if (transaction != null)
            {
                await transaction.CommitAsync(ct);
            }

            return new StockMoveResult(
                movement.StockMovementID, productId, warehouseId,
                item.QuantityOnHand, item.QuantityReserved, totalStock);
        }
        catch
        {
            if (transaction != null)
            {
                // Sin CancellationToken a propósito: el rollback tiene que ocurrir igual.
                await transaction.RollbackAsync();
            }
            throw;
        }
        finally
        {
            if (transaction != null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// <c>Product.Stock</c> = SUM(<c>QuantityOnHand − QuantityReserved</c>) de todas sus bodegas.
    ///
    /// ⚠️ NOTA DE DISEÑO (leer antes de "corregir" esto): la spec de E1 dice en un punto
    /// "SUM de QuantityOnHand", pero su propio criterio de aceptación dice "el catálogo
    /// público sigue mostrando DISPONIBILIDAD correcta usando Product.Stock", y la regla de
    /// Wompi dice que la reserva mueve <c>QuantityReserved</c> y no <c>QuantityOnHand</c>.
    /// Las tres cosas solo son ciertas a la vez si el total denormalizado es el DISPONIBLE.
    /// Con la suma cruda de OnHand, las unidades apartadas por un pedido esperando pago
    /// seguirían apareciendo vendibles en el catálogo durante los 45 minutos de la reserva
    /// → sobreventa. Hoy el checkout descuenta al instante, así que esto además conserva
    /// el comportamiento vigente.
    /// </summary>
    private async Task<int> RecalculateProductStockAsync(Guid productId, CancellationToken ct)
    {
        // Se suma en SQL con dos agregados (QuantityAvailable es [NotMapped] y no traduce).
        var totals = await _context.StockItems
            .Where(s => s.ProductID == productId)
            .GroupBy(s => s.ProductID)
            .Select(g => new
            {
                OnHand = g.Sum(s => s.QuantityOnHand),
                Reserved = g.Sum(s => s.QuantityReserved)
            })
            .FirstOrDefaultAsync(ct);

        var total = totals == null ? 0 : totals.OnHand - totals.Reserved;
        if (total < 0) total = 0;

        var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductID == productId, ct);
        if (product != null && product.Stock != total)
        {
            product.Stock = total;
            await _context.SaveChangesAsync(ct);
        }

        return total;
    }

    private async Task<StockItem> LoadOrCreateStockItemAsync(
        Guid productId, Guid warehouseId, string user, CancellationToken ct)
    {
        var item = await FindStockItemAsync(productId, warehouseId, ct);
        if (item != null) return item;

        item = new StockItem
        {
            ProductID = productId,
            WarehouseID = warehouseId,
            QuantityOnHand = 0,
            QuantityReserved = 0,
            Ownership = StockOwnership.BeanOwned,
            Status = true,
            CreatedBy = string.IsNullOrWhiteSpace(user) ? "SYSTEM" : user,
            CreatedOn = DateTime.Now
        };

        _context.StockItems.Add(item);
        return item;
    }

    private static void Touch(StockItem item, string user)
    {
        item.ModifiedBy = string.IsNullOrWhiteSpace(user) ? "SYSTEM" : user;
        item.ModifiedOn = DateTime.Now;
    }

    /// <summary>
    /// Suelta del change tracker las entidades del par producto × bodega para que el
    /// reintento lea el saldo REAL y no la copia obsoleta que provocó el choque.
    /// </summary>
    private void DetachStockEntities(Guid productId, Guid warehouseId)
    {
        foreach (var entry in _context.ChangeTracker.Entries<StockItem>().ToList())
        {
            if (entry.Entity.ProductID == productId && entry.Entity.WarehouseID == warehouseId)
            {
                entry.State = EntityState.Detached;
            }
        }

        foreach (var entry in _context.ChangeTracker.Entries<StockMovement>().ToList())
        {
            if (entry.State == EntityState.Added
                && entry.Entity.ProductID == productId
                && entry.Entity.WarehouseID == warehouseId)
            {
                entry.State = EntityState.Detached;
            }
        }
    }

}

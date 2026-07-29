using Microsoft.EntityFrameworkCore;
using Web.Data;
using Web.Models;
using Web.Models.Enums;

namespace Web.Services.Payments;

/// <summary>
/// El corazón de la integración: aplica una <see cref="PaymentSnapshot"/> a un pedido de
/// forma IDEMPOTENTE y TRANSACCIONAL. Lo llaman las tres fuentes posibles —el webhook
/// (fuente de verdad), la ruta de retorno del cliente y el barrido de reservas vencidas—
/// para que la regla de negocio viva en UN solo sitio.
///
/// Reglas:
///  1. Solo se transiciona desde <see cref="PaymentStatus.Pending"/>. Un evento duplicado
///     o fuera de orden sobre un pago ya terminal se registra y se ignora.
///  2. El monto se verifica contra lo que se firmó (<c>Order.PaymentAmountInCents</c>).
///     Si no coincide, NO se aprueba: se marca para revisión manual.
///  3. El reintegro de stock ocurre ÚNICAMENTE al transicionar hacia
///     <see cref="OrderStatus.Cancelled"/> desde un estado distinto de Cancelled, y
///     reutilizando la regla ya existente de <see cref="OrderWorkflow.ShouldRestock"/>.
/// </summary>
public sealed class PaymentApplicationService
{
    private readonly ApplicationDbContext _context;
    private readonly IPaymentGateway _gateway;
    private readonly ILogger<PaymentApplicationService> _logger;

    public PaymentApplicationService(
        ApplicationDbContext context,
        IPaymentGateway gateway,
        ILogger<PaymentApplicationService> logger)
    {
        _context = context;
        _gateway = gateway;
        _logger = logger;
    }

    // ------------------------------------------------------------------ Aplicar

    public async Task<PaymentApplyResult> ApplyAsync(PaymentSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var order = await FindOrderAsync(snapshot.Reference, ct);
        if (order == null)
        {
            _logger.LogWarning(
                "Pago {Source}: no existe pedido para la referencia recibida. Se ignora.",
                snapshot.Source);
            return new PaymentApplyResult(PaymentApplyOutcome.OrderNotFound, null, null);
        }

        // --- Verificación de monto: la firma impide manipularlo desde el navegador,
        //     pero se comprueba igual contra lo que NOSOTROS pedimos cobrar.
        if (snapshot.AmountInCents.HasValue
            && order.PaymentAmountInCents.HasValue
            && snapshot.AmountInCents.Value != order.PaymentAmountInCents.Value)
        {
            _logger.LogError(
                "Pago {Source}: el monto notificado ({Notificado} centavos) no coincide con el cobrado " +
                "({Cobrado} centavos) en el pedido {OrderId}. NO se aprueba; requiere revisión manual.",
                snapshot.Source, snapshot.AmountInCents.Value, order.PaymentAmountInCents.Value, order.OrderID);

            order.PaymentStatusMessage = "Monto notificado distinto al cobrado. Revisión manual.";
            await _context.SaveChangesAsync(ct);

            return new PaymentApplyResult(PaymentApplyOutcome.AmountMismatch, order.OrderID, order.PaymentStatus);
        }

        // --- ¿La referencia es la del intento VIGENTE?
        //     Un evento tardío de un intento anterior solo se honra si trae dinero
        //     (APPROVED): en cualquier otro caso pisaría el intento en curso.
        var isCurrentAttempt = string.Equals(order.PaymentReference, snapshot.Reference, StringComparison.Ordinal);
        if (!isCurrentAttempt && snapshot.Status != PaymentStatus.Approved)
        {
            _logger.LogInformation(
                "Pago {Source}: evento de un intento anterior del pedido {OrderId} con estado {Estado}. Se ignora.",
                snapshot.Source, order.OrderID, snapshot.Status);
            return new PaymentApplyResult(PaymentApplyOutcome.Ignored, order.OrderID, order.PaymentStatus);
        }

        // --- Idempotencia: los estados terminales son inmutables.
        if (order.PaymentStatus != PaymentStatus.Pending)
        {
            if (order.PaymentStatus == snapshot.Status)
            {
                return new PaymentApplyResult(PaymentApplyOutcome.NoChange, order.OrderID, order.PaymentStatus);
            }

            if (snapshot.Status != PaymentStatus.Approved)
            {
                _logger.LogInformation(
                    "Pago {Source}: el pedido {OrderId} ya estaba en {Actual}; se ignora el estado {Nuevo}.",
                    snapshot.Source, order.OrderID, order.PaymentStatus, snapshot.Status);
                return new PaymentApplyResult(PaymentApplyOutcome.Ignored, order.OrderID, order.PaymentStatus);
            }

            // Aprobado sobre un pago ya cerrado (típicamente expirado): el dinero SÍ se
            // movió. Se registra para que el back-office lo vea, pero no se resucita el
            // pedido automáticamente (el stock puede haberse vendido a otro cliente).
            _logger.LogError(
                "Pago {Source}: llegó APPROVED para el pedido {OrderId}, que estaba en {Actual}. " +
                "Requiere revisión manual (posible pago sobre reserva vencida).",
                snapshot.Source, order.OrderID, order.PaymentStatus);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            RecordTransactionData(order, snapshot);

            PaymentApplyOutcome outcome;

            switch (snapshot.Status)
            {
                case PaymentStatus.Approved:
                    order.PaymentStatus = PaymentStatus.Approved;
                    order.PaidAt = DateTime.Now;
                    // Si el dinero entró por un intento anterior, la referencia buena es
                    // ésa: es la que hay que conciliar contra el extracto de Wompi.
                    order.PaymentReference = snapshot.Reference;

                    if (order.OrderStatus == OrderStatus.Pending)
                    {
                        order.OrderStatus = OrderStatus.Confirmed;
                    }
                    else if (order.OrderStatus == OrderStatus.Cancelled)
                    {
                        order.PaymentStatusMessage =
                            "Pago aprobado sobre un pedido ya cancelado. Revisar y reembolsar o reactivar a mano.";
                    }

                    outcome = PaymentApplyOutcome.Approved;
                    break;

                case PaymentStatus.Declined:
                case PaymentStatus.Error:
                    // El pedido SIGUE Pending y el stock SIGUE reservado a propósito:
                    // lo normal es que el cliente reintente enseguida con otro medio.
                    order.PaymentStatus = snapshot.Status;
                    outcome = snapshot.Status == PaymentStatus.Declined
                        ? PaymentApplyOutcome.Declined
                        : PaymentApplyOutcome.Errored;
                    break;

                case PaymentStatus.Voided:
                    order.PaymentStatus = PaymentStatus.Voided;
                    await CancelAndRestockAsync(order, ct);
                    outcome = PaymentApplyOutcome.Voided;
                    break;

                default:
                    // PENDING: la transacción existe pero no ha resuelto. Solo se guarda
                    // el id para poder reconciliar más tarde.
                    outcome = PaymentApplyOutcome.NoChange;
                    break;
            }

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return new PaymentApplyResult(outcome, order.OrderID, order.PaymentStatus);
        }
        catch (Exception ex)
        {
            // Sin CancellationToken a propósito: el rollback tiene que ocurrir igual.
            await transaction.RollbackAsync();
            _logger.LogError(ex, "No se pudo aplicar el resultado del pago al pedido {OrderId}.", order.OrderID);
            throw;
        }
    }

    // --------------------------------------------------------------- Expiración

    /// <summary>
    /// Vence la reserva de un pedido que nunca se pagó: cancela y devuelve el stock.
    /// Idempotente: si el pedido ya no está pendiente de pago, no hace nada.
    /// </summary>
    public async Task<bool> ExpireAsync(Order order, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.PaymentStatus == PaymentStatus.Approved) return false;
        if (order.OrderStatus == OrderStatus.Cancelled) return false;

        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            order.PaymentStatus = PaymentStatus.Expired;
            order.PaymentStatusMessage = "La reserva venció sin completarse el pago.";
            await CancelAndRestockAsync(order, ct);

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            _logger.LogInformation("Pedido {OrderId} vencido sin pago: se liberó la reserva de stock.", order.OrderID);
            return true;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "No se pudo vencer la reserva del pedido {OrderId}.", order.OrderID);
            return false;
        }
    }

    /// <summary>
    /// Cancelación pedida por el propio cliente desde la confirmación
    /// ("no voy a pagar"). Libera la reserva al instante.
    /// </summary>
    public async Task<bool> CancelUnpaidAsync(Order order, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.PaymentStatus == PaymentStatus.Approved) return false;
        if (order.OrderStatus == OrderStatus.Cancelled) return false;
        if (order.OrderStatus != OrderStatus.Pending) return false;

        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            order.PaymentStatus = PaymentStatus.Expired;
            order.PaymentStatusMessage = "El cliente canceló el pedido antes de pagar.";
            await CancelAndRestockAsync(order, ct);

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "No se pudo cancelar el pedido sin pagar {OrderId}.", order.OrderID);
            return false;
        }
    }

    // ------------------------------------------------------------------ Helpers

    /// <summary>
    /// Busca el pedido por la referencia vigente y, si no la encuentra (webhook de un
    /// intento anterior), la resuelve descomponiendo la referencia
    /// <c>BEAN-{OrderID:N}-{intento}</c>. Por eso no hace falta una tabla de intentos.
    /// </summary>
    private async Task<Order?> FindOrderAsync(string reference, CancellationToken ct)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.PaymentReference == reference, ct);

        if (order != null) return order;

        if (_gateway.TryParseReference(reference, out var orderId, out _))
        {
            return await _context.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.OrderID == orderId, ct);
        }

        return null;
    }

    private static void RecordTransactionData(Order order, PaymentSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.TransactionId))
        {
            order.PaymentTransactionId = snapshot.TransactionId;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.PaymentMethodType))
        {
            order.PaymentMethodUsed = snapshot.PaymentMethodType;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Environment))
        {
            order.PaymentEnvironment = snapshot.Environment;
        }

        order.PaymentStatusMessage = string.IsNullOrWhiteSpace(snapshot.StatusMessage)
            ? order.PaymentStatusMessage
            : snapshot.StatusMessage;
    }

    /// <summary>
    /// ÚNICA ruta de reintegro de stock de la integración de pagos: solo al pasar a
    /// Cancelled desde un estado distinto de Cancelled, y respetando
    /// <see cref="OrderWorkflow.ShouldRestock"/> (si el pedido ya salió de bodega el
    /// ajuste es manual).
    /// </summary>
    private async Task CancelAndRestockAsync(Order order, CancellationToken ct)
    {
        if (order.OrderStatus == OrderStatus.Cancelled) return;

        if (OrderWorkflow.ShouldRestock(order.OrderStatus))
        {
            if (!_context.Entry(order).Collection(o => o.Items).IsLoaded)
            {
                await _context.Entry(order).Collection(o => o.Items).LoadAsync(ct);
            }

            foreach (var item in order.Items)
            {
                var product = await _context.Products.FindAsync(new object?[] { item.ProductID }, ct);
                if (product != null)
                {
                    product.Stock += item.Quantity;
                }
            }
        }

        order.OrderStatus = OrderStatus.Cancelled;
    }
}

public enum PaymentApplyOutcome
{
    /// <summary>La referencia no corresponde a ningún pedido nuestro.</summary>
    OrderNotFound,

    /// <summary>El monto notificado no coincide con el cobrado: no se aprueba.</summary>
    AmountMismatch,

    /// <summary>Evento antiguo o sobre un pago ya terminal.</summary>
    Ignored,

    /// <summary>Se registró información pero el estado no cambió.</summary>
    NoChange,

    Approved,
    Declined,
    Errored,
    Voided
}

public sealed record PaymentApplyResult(PaymentApplyOutcome Outcome, Guid? OrderId, PaymentStatus? Status);

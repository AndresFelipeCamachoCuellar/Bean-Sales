using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Web.Data;
using Web.Models.Enums;

namespace Web.Services.Payments;

/// <summary>
/// Libera las reservas de stock de los pedidos que nunca se pagaron.
///
/// **Barrido perezoso, NO un IHostedService**: el hosting es MonsterASP plan Free
/// (256 MB, con reciclado de app pool y cold start), donde un background service se
/// muere sin aviso. En su lugar se invoca de forma oportunista y acotada desde el
/// GET de <c>Cart/Checkout</c> —justo antes de que alguien compre, que es cuando más
/// importa que el stock esté libre— con un cooldown en memoria para no golpear la BD.
///
/// Antes de cancelar, si el pedido tiene una transacción en Wompi se RECONCILIA contra
/// el API: nunca se cancela un pedido que en realidad sí se pagó y cuyo webhook se perdió.
/// </summary>
public sealed class PendingOrderExpirationService
{
    private const string CooldownKey = "payments:expiration:lastsweep";
    private const int CooldownMinutes = 5;
    private const int MaxOrdersPerSweep = 10;

    /// <summary>
    /// Tope de llamadas HTTP a Wompi por barrido. El barrido corre DENTRO de una
    /// petición del cliente (GET Cart/Checkout): sin este tope, 10 reconciliaciones a
    /// 8 s de timeout podrían dejar la página colgada más de un minuto.
    /// </summary>
    private const int MaxReconciliationsPerSweep = 3;

    private readonly ApplicationDbContext _context;
    private readonly PaymentApplicationService _payments;
    private readonly IPaymentGateway _gateway;
    private readonly IMemoryCache _cache;
    private readonly WompiOptions _options;
    private readonly ILogger<PendingOrderExpirationService> _logger;

    public PendingOrderExpirationService(
        ApplicationDbContext context,
        PaymentApplicationService payments,
        IPaymentGateway gateway,
        IMemoryCache cache,
        IOptions<WompiOptions> options,
        ILogger<PendingOrderExpirationService> logger)
    {
        _context = context;
        _payments = payments;
        _gateway = gateway;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Barrido acotado. Nunca lanza: es una tarea de mantenimiento, no puede tumbar
    /// la página desde la que se invoca.
    /// </summary>
    public async Task SweepAsync(CancellationToken ct = default)
    {
        if (!_options.CanCharge) return;               // sin pasarela no hay reservas que vencer
        if (_cache.TryGetValue(CooldownKey, out _)) return;

        _cache.Set(CooldownKey, DateTime.UtcNow, TimeSpan.FromMinutes(CooldownMinutes));

        try
        {
            var now = DateTime.Now;

            var vencidos = await _context.Orders
                .Include(o => o.Items)
                .Where(o => o.OrderStatus == OrderStatus.Pending
                            && o.PaymentStatus != PaymentStatus.Approved
                            && o.PaymentExpiresAt != null
                            && o.PaymentExpiresAt < now)
                .OrderBy(o => o.PaymentExpiresAt)
                .Take(MaxOrdersPerSweep)
                .ToListAsync(ct);

            var reconciliaciones = 0;

            foreach (var order in vencidos)
            {
                // Reconciliación previa: si Wompi dice que sí se pagó, se confirma en
                // vez de cancelarse (red de seguridad ante un webhook perdido).
                // Solo tiene sentido si llegó a existir una transacción sin resolver:
                // un carrito abandonado nunca tiene PaymentTransactionId.
                if (order.PaymentStatus == PaymentStatus.Pending
                    && !string.IsNullOrWhiteSpace(order.PaymentTransactionId)
                    && reconciliaciones < MaxReconciliationsPerSweep)
                {
                    reconciliaciones++;

                    var snapshot = await _gateway.GetTransactionAsync(order.PaymentTransactionId!, ct);
                    if (snapshot is not null && snapshot.Status == PaymentStatus.Approved)
                    {
                        await _payments.ApplyAsync(snapshot, ct);
                        continue;
                    }
                }

                await _payments.ExpireAsync(order, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "El barrido de reservas de pago vencidas no pudo completarse.");
        }
    }
}

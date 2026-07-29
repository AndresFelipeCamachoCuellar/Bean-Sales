using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Web.Attributes;
using Web.Constants;
using Web.Data;
using Web.Models.Enums;
using Web.Models.ViewModels;
using Web.Services;

namespace Web.Controllers;

/// <summary>
/// Bandeja de pedidos del back-office: ver, avanzar de estado y cancelar (con reintegro de stock).
/// No toca el flujo del cliente (OrdersController / CartController).
/// </summary>
[Authorize]
public class OrderManagementController : Controller
{
    private const int PageSize = 25;

    private readonly ApplicationDbContext _context;

    public OrderManagementController(ApplicationDbContext context)
    {
        _context = context;
    }

    // ---------------------------------------------------------------- Index
    [HasPermission(Modules.Orders, Permissions.Read)]
    public async Task<IActionResult> Index(string? q, OrderStatus? estado, int page = 1)
    {
        if (page < 1) page = 1;

        // Contadores de todas las pestañas en UNA sola query agrupada.
        var counts = await _context.Orders
            .GroupBy(o => o.OrderStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        int CountOf(OrderStatus s)
        {
            var row = counts.FirstOrDefault(c => c.Status == s);
            return row == null ? 0 : row.Count;
        }

        var vm = new OrderManagementViewModel
        {
            Query = q,
            Estado = estado,
            Page = page,
            PageSize = PageSize,
            CountAll = counts.Sum(c => c.Count),
            CountNew = CountOf(OrderStatus.Pending) + CountOf(OrderStatus.Confirmed),
            CountProcessing = CountOf(OrderStatus.Processing),
            CountShipped = CountOf(OrderStatus.Shipped),
            CountDelivered = CountOf(OrderStatus.Delivered),
            CountCancelled = CountOf(OrderStatus.Cancelled)
        };

        var query = _context.Orders.AsQueryable();

        // Búsqueda: por cliente (nombre/apellido) o correo. Si el término es un GUID
        // completo se busca el pedido exacto (evitamos traducir Guid.ToString() a SQL).
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            if (Guid.TryParse(term, out var orderId))
            {
                query = query.Where(o => o.OrderID == orderId);
            }
            else
            {
                query = query.Where(o =>
                    o.FirstName.Contains(term) ||
                    o.LastName.Contains(term) ||
                    (o.User != null && o.User.Email != null && o.User.Email.Contains(term)));
            }
        }

        // Pestañas. "Nuevos" (estado=Confirmed) agrupa Pending + Confirmed.
        if (estado.HasValue)
        {
            if (estado.Value == OrderStatus.Confirmed)
            {
                query = query.Where(o =>
                    o.OrderStatus == OrderStatus.Pending || o.OrderStatus == OrderStatus.Confirmed);
            }
            else
            {
                var target = estado.Value;
                query = query.Where(o => o.OrderStatus == target);
            }
        }

        vm.TotalCount = await query.CountAsync();

        var totalPages = vm.TotalPages;
        if (page > totalPages) page = totalPages;
        vm.Page = page;

        vm.Orders = await query
            .OrderByDescending(o => o.OrderDate)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(o => new OrderRowViewModel
            {
                OrderID = o.OrderID,
                FirstName = o.FirstName,
                LastName = o.LastName,
                Email = o.User!.Email,
                OrderDate = o.OrderDate,
                Status = o.OrderStatus,
                TotalAmount = o.TotalAmount,
                CurrencyCode = o.CurrencyCode,
                Bags = o.Items.Sum(i => (int?)i.Quantity) ?? 0,
                Lines = o.Items.Count()
            })
            .ToListAsync();

        return View(vm);
    }

    // -------------------------------------------------------------- Details
    [HasPermission(Modules.Orders, Permissions.Read)]
    public async Task<IActionResult> Details(Guid id)
    {
        var order = await _context.Orders
            .Include(o => o.User)
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p!.Provider)
            .FirstOrDefaultAsync(o => o.OrderID == id);

        if (order == null) return NotFound();

        var vm = new OrderDetailViewModel
        {
            Order = order,
            NextStates = OrderWorkflow.NextStates(order.OrderStatus),
            CanCancel = OrderWorkflow.CanCancel(order.OrderStatus),
            WillRestock = OrderWorkflow.ShouldRestock(order.OrderStatus),
            UnitsToRestock = order.Items.Sum(i => i.Quantity)
        };

        return View(vm);
    }

    // --------------------------------------------------------- UpdateStatus
    [HasPermission(Modules.Orders, Permissions.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(Guid id, OrderStatus nuevoEstado, OrderStatus estadoActual, string? returnUrl)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.OrderID == id);
        if (order == null) return NotFound();

        // "Token" de concurrencia optimista: el estado que veía el operador debe seguir vigente.
        if (order.OrderStatus != estadoActual)
        {
            TempData["OrderError"] = "El pedido cambió de estado mientras lo revisabas. Vuelve a intentarlo.";
            return Back(returnUrl);
        }

        if (!OrderWorkflow.NextStates(estadoActual).Contains(nuevoEstado))
        {
            TempData["OrderError"] = "Esa transición de estado no está permitida.";
            return Back(returnUrl);
        }

        // Un pedido en Pending está esperando el PAGO: confirmarlo a mano sería despachar
        // sin cobrar. La verdad del pago la ponen el webhook de Wompi o la reconsulta.
        if (!OrderWorkflow.CanAdvance(estadoActual, order.PaymentStatus))
        {
            TempData["OrderError"] = "Este pedido todavía no está pagado: no se puede confirmar a mano.";
            return Back(returnUrl);
        }

        order.OrderStatus = nuevoEstado;
        await _context.SaveChangesAsync();

        TempData["OrderOk"] = $"Pedido #{ShortId(order.OrderID)} actualizado a “{OrderStatusUi.For(nuevoEstado).Label}”.";
        return Back(returnUrl);
    }

    // --------------------------------------------------------------- Cancel
    [HasPermission(Modules.Orders, Permissions.Delete)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id, OrderStatus estadoActual)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var order = await _context.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.OrderID == id);

            if (order == null) return NotFound();

            if (order.OrderStatus != estadoActual)
            {
                TempData["OrderError"] = "El pedido cambió de estado mientras lo revisabas. Vuelve a intentarlo.";
                return RedirectToAction(nameof(Details), new { id });
            }

            if (!OrderWorkflow.CanCancel(order.OrderStatus))
            {
                TempData["OrderError"] = "Este pedido ya no se puede cancelar.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var restock = OrderWorkflow.ShouldRestock(order.OrderStatus);
            var units = 0;

            if (restock)
            {
                foreach (var item in order.Items)
                {
                    var p = await _context.Products.FindAsync(item.ProductID);
                    if (p != null)
                    {
                        p.Stock += item.Quantity;
                        units += item.Quantity;
                    }
                }
            }

            order.OrderStatus = OrderStatus.Cancelled;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["OrderOk"] = restock
                ? $"Pedido #{ShortId(order.OrderID)} cancelado. Se devolvieron {units} unidades al inventario."
                : $"Pedido #{ShortId(order.OrderID)} cancelado. El inventario NO se reintegró (el pedido ya había salido de bodega): ajústalo a mano cuando recibas la devolución.";
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            TempData["OrderError"] = "No se pudo cancelar el pedido. No se aplicó ningún cambio.";
        }

        return RedirectToAction(nameof(Index));
    }

    // -------------------------------------------------------------- Helpers
    private IActionResult Back(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction(nameof(Index));
    }

    private static string ShortId(Guid id) => id.ToString("N").Substring(0, 8).ToUpperInvariant();
}

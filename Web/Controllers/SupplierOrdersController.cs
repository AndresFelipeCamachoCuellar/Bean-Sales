using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Web.Attributes;
using Web.Constants;
using Web.Data;
using Web.Models;
using Web.Models.Enums;
using Web.Models.ViewModels;
using Web.Services.Tenancy;

namespace Web.Controllers;

// Pedidos entrantes del proveedor (handoff vista 13, nueva vista).
// Muestra los pedidos que contienen productos del proveedor del usuario actual,
// con "Tu pago" = suma de subtotales de SUS líneas (no el total del pedido, que
// puede incluir productos de otros proveedores en un carrito multi-marca).
//
// Scoping por proveedor: desde E5.2 lo aplica IProviderScope. Se protege con el mismo
// permiso Products/Read que usa el proveedor para su catálogo.
//
// ⚠️ OJO al refactorizar: aquí NO se filtran "pedidos del proveedor" (Order no tiene
// ProviderID), sino "pedidos que TOCAN al menos un producto del proveedor". Por eso el
// filtro va sobre la subconsulta de líneas (scope.ApplyTo(_context.Products)) y NO sobre
// _context.Orders. Un pedido multi-marca entra en la lista de los DOS proveedores, y cada
// uno solo puede ver sus propias líneas (ProviderItems).
[Authorize]
public class SupplierOrdersController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IProviderScope _scope;

    public SupplierOrdersController(ApplicationDbContext context, IProviderScope scope)
    {
        _context = context;
        _scope = scope;
    }

    // GET /SupplierOrders  → cola de pedidos entrantes del proveedor actual.
    [HasPermission(Modules.Products, Permissions.Read)]
    public async Task<IActionResult> Index()
    {
        var scope = await _scope.RequireProviderAsync();
        if (scope == null) return Forbid();

        var providerId = scope.CurrentProviderId!.Value;

        // Ids de los lotes del proveedor. Es una SUBCONSULTA (no se materializa): EF la
        // traduce a un EXISTS/IN contra la tabla de productos.
        var myProductIds = scope.ApplyTo(_context.Products).Select(p => p.ProductID);

        // Pedidos que incluyen al menos una línea de un producto de este proveedor.
        var orders = await _context.Orders
            .Include(o => o.User)
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            // Solo pedidos PAGADOS: el proveedor no debe preparar café de un pedido que
            // todavía está esperando el pago (o que se abandonó en la pasarela).
            .Where(o => o.PaymentStatus == PaymentStatus.Approved
                        && o.Items.Any(i => myProductIds.Contains(i.ProductID)))
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync();

        // Proyección en memoria (ya materializado): filtra las líneas del proveedor
        // y calcula "Tu pago" solo con esas líneas.
        var model = orders.Select(o =>
        {
            var providerItems = o.Items
                .Where(i => i.Product != null && i.Product.ProviderID == providerId)
                .ToList();

            return new SupplierOrderViewModel
            {
                Order = o,
                ProviderItems = providerItems,
                // ⚠️ E2 (ago-2026): "Tu pago" NO es el subtotal de la venta. Desde que se
                // separó el costo del PVP, lo que Bean le debe al proveedor es su COSTO
                // congelado en el momento de la compra (SupplierPriceSnapshot × cantidad).
                // Usar SubTotal aquí le mostraría al proveedor el precio de venta de Bean,
                // que además incluye el margen. Los pedidos anteriores a la migración
                // tienen SupplierPriceSnapshot = UnitPrice, así que su cifra no cambia.
                Payout = providerItems.Sum(i => i.SupplierPriceSnapshot * i.Quantity),
                Group = GroupOf(o.OrderStatus)
            };
        }).ToList();

        return View(model);
    }

    // Mapea el estado global del pedido a la cola operativa del proveedor.
    // NOTA: hoy OrderStatus es del pedido completo (no hay un estado por-proveedor);
    // el "hand-off" real del proveedor (marcar listo / registrar guía) se gestiona
    // a nivel de Product en el flujo de aprobación. Aquí solo agrupamos para la cola.
    private static string GroupOf(OrderStatus status) => status switch
    {
        OrderStatus.Shipped => "listos",        // listo para recogida / en camino
        OrderStatus.Delivered => "completados",
        OrderStatus.Cancelled => "completados",
        _ => "preparar"                          // Pending / Confirmed / Processing
    };
}

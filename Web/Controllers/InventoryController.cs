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
using Web.Services;
using Web.Services.Inventory;

namespace Web.Controllers;

/// <summary>
/// Inventario multi-bodega (épica E1, tanda 3B). Tres pantallas:
///  · <c>Index</c>   — matriz producto × bodega: "¿qué puedo vender y dónde está?".
///  · <c>Details</c> — auditoría de un lote: saldo por bodega + libro mayor.
///  · <c>Adjust</c>  — cuadre manual con razón OBLIGATORIA.
///
/// Nada de esto escribe stock por su cuenta: todo pasa por <see cref="InventoryService"/>,
/// que es el único punto autorizado a tocar saldos.
/// </summary>
[Authorize]
public class InventoryController : Controller
{
    private const int PageSize = InventoryMatrixViewModel.PageSizeValue;
    private const int MovementsPageSize = InventoryDetailsViewModel.PageSizeValue;

    private readonly ApplicationDbContext _context;
    private readonly InventoryService _inventory;
    private readonly IPermissionService _permissions;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<InventoryController> _logger;

    public InventoryController(
        ApplicationDbContext context,
        InventoryService inventory,
        IPermissionService permissions,
        UserManager<ApplicationUser> userManager,
        ILogger<InventoryController> logger)
    {
        _context = context;
        _inventory = inventory;
        _permissions = permissions;
        _userManager = userManager;
        _logger = logger;
    }

    // ================================================================== Index

    /// <summary>
    /// Matriz de disponibilidad. Filas = lote (paginadas de 25), columnas = bodega activa,
    /// celda = disponible. Los totales por columna se calculan sobre TODO el conjunto
    /// filtrado, no solo sobre la página visible.
    /// </summary>
    [HasPermission(Modules.Inventory, Permissions.Read)]
    public async Task<IActionResult> Index(
        Guid? bodega, Guid? proveedor, ProductStatus? estado, bool bajo = false, string? q = null, int page = 1)
    {
        if (page < 1) page = 1;

        var vm = new InventoryMatrixViewModel
        {
            WarehouseID = bodega,
            ProviderID = proveedor,
            Estado = estado,
            SoloStockBajo = bajo,
            Query = q,
            Page = page
        };

        // ---- Columnas: bodegas operativas. Si se filtra por una, la matriz se reduce a ella.
        var warehouseQuery = _context.Warehouses.Where(w => w.Status && w.IsActive);
        if (bodega.HasValue)
        {
            var wid = bodega.Value;
            warehouseQuery = warehouseQuery.Where(w => w.WarehouseID == wid);
        }

        var warehouses = await warehouseQuery
            .OrderByDescending(w => w.IsDefault)
            .ThenBy(w => w.Code)
            .Select(w => new InventoryColumnViewModel
            {
                WarehouseID = w.WarehouseID,
                Code = w.Code,
                Name = w.Name,
                City = w.City
            })
            .ToListAsync();

        vm.Columns = warehouses;

        // Opciones de los desplegables de filtro (siempre todas las bodegas operativas).
        vm.WarehouseOptions = await _context.Warehouses
            .Where(w => w.Status && w.IsActive)
            .OrderByDescending(w => w.IsDefault)
            .ThenBy(w => w.Code)
            .Select(w => new InventoryFilterOption
            {
                Id = w.WarehouseID,
                Label = w.Code + " · " + w.Name
            })
            .ToListAsync();

        vm.ProviderOptions = await _context.Providers
            .Where(p => p.Status)
            .OrderBy(p => p.Name)
            .Select(p => new InventoryFilterOption { Id = p.ProviderID, Label = p.Name })
            .ToListAsync();

        // Lotes bajo el punto de reorden en TODO el inventario (alimenta el aviso de la vista).
        vm.LowStockCount = await CountLowStockProductsAsync(_context);

        if (warehouses.Count == 0)
        {
            // Sin bodegas la matriz no tiene columnas: la vista muestra el estado vacío
            // con el enlace para crear la primera.
            return View(vm);
        }

        var warehouseIds = warehouses.Select(w => w.WarehouseID).ToList();

        // ---- Filas: catálogo filtrado.
        var query = _context.Products.Where(p => p.Status);

        if (proveedor.HasValue)
        {
            var pid = proveedor.Value;
            query = query.Where(p => p.ProviderID == pid);
        }

        if (estado.HasValue)
        {
            var st = estado.Value;
            query = query.Where(p => p.ProductStatus == st);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(p => p.Name.Contains(term) || (p.Lot != null && p.Lot.Contains(term)));
        }

        if (bajo)
        {
            // "Solo stock bajo": al menos una de las bodegas visibles está en el punto de
            // reorden o por debajo. Sin ReorderPoint no hay umbral que romper.
            query = query.Where(p => p.StockItems.Any(s =>
                warehouseIds.Contains(s.WarehouseID)
                && s.ReorderPoint != null
                && (s.QuantityOnHand - s.QuantityReserved) <= s.ReorderPoint));
        }

        vm.TotalCount = await query.CountAsync();

        var totalPages = vm.TotalPages;
        if (page > totalPages) page = totalPages;
        vm.Page = page;

        // ---- Totales por columna sobre el conjunto FILTRADO (subconsulta, no la página).
        var filteredProductIds = query.Select(p => p.ProductID);

        var columnTotals = await _context.StockItems
            .Where(s => warehouseIds.Contains(s.WarehouseID) && filteredProductIds.Contains(s.ProductID))
            .GroupBy(s => s.WarehouseID)
            .Select(g => new
            {
                WarehouseID = g.Key,
                OnHand = g.Sum(s => s.QuantityOnHand),
                Reserved = g.Sum(s => s.QuantityReserved)
            })
            .ToListAsync();

        foreach (var column in warehouses)
        {
            var total = columnTotals.FirstOrDefault(t => t.WarehouseID == column.WarehouseID);
            if (total == null) continue;

            column.TotalOnHand = total.OnHand;
            column.TotalReserved = total.Reserved;
            column.TotalAvailable = total.OnHand - total.Reserved;
        }

        // ---- Página de filas.
        var rows = await query
            .OrderBy(p => p.Name)
            .ThenBy(p => p.ProductID)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(p => new InventoryRowViewModel
            {
                ProductID = p.ProductID,
                Name = p.Name,
                Lot = p.Lot,
                ProviderName = p.Provider != null ? p.Provider.Name : null,
                ProductStatus = p.ProductStatus,
                ProductStock = p.Stock
            })
            .ToListAsync();

        var pageProductIds = rows.Select(r => r.ProductID).ToList();

        // Una sola query para todas las celdas de la página.
        var cells = await _context.StockItems
            .Where(s => pageProductIds.Contains(s.ProductID) && warehouseIds.Contains(s.WarehouseID))
            .Select(s => new
            {
                s.ProductID,
                s.WarehouseID,
                s.QuantityOnHand,
                s.QuantityReserved,
                s.ReorderPoint,
                s.Ownership
            })
            .ToListAsync();

        foreach (var row in rows)
        {
            foreach (var cell in cells.Where(c => c.ProductID == row.ProductID))
            {
                row.Cells[cell.WarehouseID] = new InventoryCellViewModel
                {
                    Exists = true,
                    QuantityOnHand = cell.QuantityOnHand,
                    QuantityReserved = cell.QuantityReserved,
                    ReorderPoint = cell.ReorderPoint,
                    Ownership = cell.Ownership
                };

                row.TotalOnHand += cell.QuantityOnHand;
                row.TotalReserved += cell.QuantityReserved;
            }
        }

        vm.Rows = rows;

        return View(vm);
    }

    // ================================================================ Details

    /// <summary>
    /// Auditoría de un lote (HU-1.2): saldo por bodega + libro mayor paginado. El libro es
    /// inmutable, así que esta pantalla explica cualquier diferencia de conteo.
    /// </summary>
    [HasPermission(Modules.Inventory, Permissions.Read)]
    public async Task<IActionResult> Details(Guid id, int page = 1)
    {
        if (page < 1) page = 1;

        var product = await _context.Products
            .Include(p => p.Provider)
            .FirstOrDefaultAsync(p => p.ProductID == id);

        if (product == null) return NotFound();

        var currentUser = await _userManager.GetUserAsync(User);

        var vm = new InventoryDetailsViewModel
        {
            ProductID = product.ProductID,
            Name = product.Name,
            Lot = product.Lot,
            ProviderName = product.Provider?.Name,
            ProductStatus = product.ProductStatus,
            ProductStock = product.Stock,
            Page = page,
            CanAdjust = currentUser != null
                        && await _permissions.HasPermissionAsync(currentUser, Modules.Inventory, Permissions.Update)
        };

        // ---- Saldo por bodega. Se listan TODAS las bodegas operativas aunque el lote no
        //      tenga saldo en alguna: así se ve dónde falta café, no solo dónde hay.
        var warehouses = await _context.Warehouses
            .Where(w => w.Status)
            .OrderByDescending(w => w.IsActive)
            .ThenByDescending(w => w.IsDefault)
            .ThenBy(w => w.Code)
            .Select(w => new { w.WarehouseID, w.Code, w.Name, w.City, w.IsActive })
            .ToListAsync();

        var stockItems = await _context.StockItems
            .Where(s => s.ProductID == id)
            .Select(s => new
            {
                s.WarehouseID,
                s.QuantityOnHand,
                s.QuantityReserved,
                s.ReorderPoint,
                s.Ownership
            })
            .ToListAsync();

        foreach (var w in warehouses)
        {
            var item = stockItems.FirstOrDefault(s => s.WarehouseID == w.WarehouseID);

            // Una bodega inactiva sin saldo no aporta nada al diagnóstico: se omite.
            if (item == null && !w.IsActive) continue;

            vm.Balances.Add(new InventoryBalanceRowViewModel
            {
                WarehouseID = w.WarehouseID,
                Code = w.Code,
                Name = w.Name,
                City = w.City,
                WarehouseIsActive = w.IsActive,
                HasStockItem = item != null,
                QuantityOnHand = item?.QuantityOnHand ?? 0,
                QuantityReserved = item?.QuantityReserved ?? 0,
                ReorderPoint = item?.ReorderPoint,
                Ownership = item?.Ownership ?? StockOwnership.BeanOwned
            });

            if (w.IsActive)
            {
                vm.WarehouseOptions.Add(new InventoryFilterOption
                {
                    Id = w.WarehouseID,
                    Label = w.Code + " · " + w.Name
                });
            }
        }

        // ---- Libro mayor paginado. La tabla crece sin techo y el hosting es de 256 MB:
        //      NUNCA se materializa entera.
        var movementsQuery = _context.StockMovements.Where(m => m.ProductID == id);

        vm.TotalMovements = await movementsQuery.CountAsync();

        var totalPages = vm.TotalPages;
        if (page > totalPages) page = totalPages;
        vm.Page = page;

        vm.Movements = await movementsQuery
            .OrderByDescending(m => m.CreatedOn)
            .ThenByDescending(m => m.StockMovementID)
            .Skip((vm.Page - 1) * MovementsPageSize)
            .Take(MovementsPageSize)
            .Select(m => new InventoryMovementRowViewModel
            {
                StockMovementID = m.StockMovementID,
                CreatedOn = m.CreatedOn,
                MovementType = m.MovementType,
                Quantity = m.Quantity,
                WarehouseCode = m.Warehouse != null ? m.Warehouse.Code : "—",
                ReferenceType = m.ReferenceType,
                ReferenceID = m.ReferenceID,
                Reason = m.Reason,
                UnitCost = m.UnitCost,
                CreatedBy = m.CreatedBy
            })
            .ToListAsync();

        return View(vm);
    }

    // ================================================================= Adjust

    /// <summary>
    /// Cuadre manual (HU-1.4). La RAZÓN es obligatoria y se valida aquí, en el servidor:
    /// el <c>required</c> del formulario es comodidad, no seguridad. El asiento queda en el
    /// libro mayor para siempre.
    /// </summary>
    [HasPermission(Modules.Inventory, Permissions.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Adjust(Guid id, Guid warehouseId, int quantity, string? reason)
    {
        var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductID == id);
        if (product == null) return NotFound();

        if (warehouseId == Guid.Empty)
        {
            TempData["InventoryError"] = "Selecciona la bodega sobre la que quieres ajustar.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (quantity == 0)
        {
            TempData["InventoryError"] = "El ajuste debe ser distinto de 0 (positivo entra, negativo sale).";
            return RedirectToAction(nameof(Details), new { id });
        }

        // Validación de servidor: sin motivo no hay ajuste. StockLedger también lo exige,
        // pero se comprueba antes para devolver un mensaje en español y no una excepción.
        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["InventoryError"] =
                "El motivo del ajuste es obligatorio: es la única forma de auditar el descuadre después.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var usuario = User.Identity?.Name ?? "SYSTEM";

        try
        {
            var result = await _inventory.MoveStockAsync(
                product.ProductID,
                warehouseId,
                StockMovementType.Adjustment,
                quantity,                       // firmado: + entra, − sale
                StockReference.None,
                reason.Trim(),
                unitCost: null,
                user: usuario);

            _logger.LogInformation(
                "Ajuste manual de inventario: lote {ProductId} en bodega {WarehouseId}, {Quantity} unidades, " +
                "por {Usuario}. Motivo: {Motivo}. Saldo resultante {OnHand} en mano / {Reserved} reservadas.",
                product.ProductID, warehouseId, quantity, usuario, reason.Trim(),
                result.QuantityOnHand, result.QuantityReserved);

            var signo = quantity > 0 ? "+" : string.Empty;
            TempData["InventoryOk"] =
                $"Ajuste registrado ({signo}{quantity} unidades). Nuevo saldo: {result.QuantityOnHand} en mano, " +
                $"{result.QuantityReserved} reservadas. Disponible total del lote: {result.ProductTotalStock}.";
        }
        catch (InventoryException ex)
        {
            // Movimiento imposible (dejaría el inventario en negativo, bodega inexistente…).
            TempData["InventoryError"] = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Falló el ajuste de inventario del lote {ProductId} en la bodega {WarehouseId}.",
                product.ProductID, warehouseId);

            TempData["InventoryError"] = "No se pudo registrar el ajuste. No se aplicó ningún cambio.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    // ======================================================= SetReorderPoint

    /// <summary>
    /// Define el punto de reorden de un lote en una bodega. NO mueve stock (no toca
    /// <c>QuantityOnHand</c> ni <c>QuantityReserved</c>), así que no pasa por
    /// <see cref="InventoryService"/>: solo configura el umbral que dispara la alerta de
    /// stock bajo. Sin esta pantalla el umbral no se podría fijar desde ningún lado y la
    /// alerta de la matriz nunca se encendería.
    /// </summary>
    [HasPermission(Modules.Inventory, Permissions.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetReorderPoint(Guid id, Guid warehouseId, int? reorderPoint)
    {
        if (reorderPoint is < 0)
        {
            TempData["InventoryError"] = "El punto de reorden no puede ser negativo.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var item = await _context.StockItems
            .FirstOrDefaultAsync(s => s.ProductID == id && s.WarehouseID == warehouseId);

        if (item == null)
        {
            TempData["InventoryError"] =
                "Este lote todavía no tiene saldo en esa bodega: registra primero una recepción o un ajuste.";
            return RedirectToAction(nameof(Details), new { id });
        }

        item.ReorderPoint = reorderPoint;
        item.ModifiedBy = User.Identity?.Name ?? "SYSTEM";
        item.ModifiedOn = DateTime.Now;

        await _context.SaveChangesAsync();

        TempData["InventoryOk"] = reorderPoint.HasValue
            ? $"Punto de reorden fijado en {reorderPoint.Value} unidades."
            : "Punto de reorden eliminado: este lote ya no dispara alerta de stock bajo.";

        return RedirectToAction(nameof(Details), new { id });
    }

    // ================================================================ Helpers

    /// <summary>
    /// Lotes DISTINTOS que están en su punto de reorden o por debajo. Lo comparten la
    /// matriz, el badge del sidebar y el KPI del dashboard: una sola definición de
    /// "stock bajo" para que los tres números no se contradigan.
    /// </summary>
    public static Task<int> CountLowStockProductsAsync(ApplicationDbContext context) =>
        context.StockItems
            .Where(s => s.ReorderPoint != null
                        && (s.QuantityOnHand - s.QuantityReserved) <= s.ReorderPoint)
            .Select(s => s.ProductID)
            .Distinct()
            .CountAsync();
}

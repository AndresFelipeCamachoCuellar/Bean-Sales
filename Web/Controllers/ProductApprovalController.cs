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
using Web.Services.Inventory;
using Web.Services.Media;
using Web.Services.Pricing;

namespace Web.Controllers;

[Authorize]
public class ProductApprovalController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ProductImageService _images;
    private readonly PricingService _pricing;
    private readonly InventoryService _inventory;
    private readonly ILogger<ProductApprovalController> _logger;

    public ProductApprovalController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        ProductImageService images,
        PricingService pricing,
        InventoryService inventory,
        ILogger<ProductApprovalController> logger)
    {
        _context = context;
        _userManager = userManager;
        _images = images;
        _pricing = pricing;
        _inventory = inventory;
        _logger = logger;
    }

    [HasPermission(Modules.ProductApprovals, Permissions.Read)]
    public async Task<IActionResult> Index()
    {
        // Show Pending, Shipped, AND Active items (to manage ongoing availability).
        var statusesOfInterest = new[] 
        { 
            ProductStatus.PendingApproval, 
            ProductStatus.ApprovedToShip, 
            ProductStatus.Shipped,
            ProductStatus.Active 
        };

        var products = await _context.Products
            .Include(p => p.Provider)
            .Include(p => p.ProductCountries)
            .ThenInclude(pc => pc.Country)
            // Include FILTRADO: solo para mostrar el contador "Fotos · N" de cada fila.
            // La miniatura sale de Product.ImageUrl (portada denormalizada), no de aquí.
            .Include(p => p.Images.Where(i => i.Status))
            .Where(p => statusesOfInterest.Contains(p.ProductStatus) && p.Status)
            .OrderBy(p => p.ProductStatus)
            .ThenByDescending(p => p.CreatedOn)
            .ToListAsync();

        return View(products);
    }

    [HasPermission(Modules.ProductApprovals, Permissions.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid id)
    {
        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.ProductID == id);

        if (product == null) return NotFound();

        // Solo se puede aprobar un producto pendiente de aprobación.
        if (product.ProductStatus != ProductStatus.PendingApproval)
            return RedirectToAction(nameof(Index));

        product.ProductStatus = ProductStatus.ApprovedToShip;
        product.RejectionReason = null;
        product.UpdatedBy = User.Identity?.Name ?? "SYSTEM";
        product.UpdatedOn = DateTime.Now;

        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    [HasPermission(Modules.ProductApprovals, Permissions.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(Guid id, string reason)
    {
        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.ProductID == id);

        if (product == null) return NotFound();

        // Solo se puede rechazar un producto pendiente de aprobación.
        if (product.ProductStatus != ProductStatus.PendingApproval)
            return RedirectToAction(nameof(Index));

        product.ProductStatus = ProductStatus.Rejected;
        product.RejectionReason = reason;
        product.UpdatedBy = User.Identity?.Name ?? "SYSTEM";
        product.UpdatedOn = DateTime.Now;

        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    [HasPermission(Modules.ProductApprovals, Permissions.Update)]
    [HttpGet]
    public async Task<IActionResult> Receive(Guid id)
    {
        var product = await _context.Products
            .Include(p => p.Provider)
            .Include(p => p.ProductCountries)
            .ThenInclude(pc => pc.Country)
            .FirstOrDefaultAsync(p => p.ProductID == id);

        if (product == null) return NotFound();

        // Allow Shipped OR Active (to update countries)
        if (product.ProductStatus != ProductStatus.Shipped && product.ProductStatus != ProductStatus.Active)
        {
            return RedirectToAction(nameof(Index));
        }

        // El momento en que el lote FÍSICO llega a bodega es el momento en que Bean lo
        // fotografía: por eso el gestor de fotos vive también en esta pantalla, la misma
        // que activa el producto. El staff siempre puede editar (ProductApprovals/Update).
        ViewBag.ImageManager = await _images.BuildManagerAsync(product, canEdit: true, role: ImageUploader.Admin);

        // Panel de pricing de la vista (costo, margen y precio sugerido). La vista decide
        // qué muestra según el permiso Pricing/Read; aquí solo se pasan los umbrales.
        var settings = await _pricing.GetSettingsAsync();
        ViewBag.TargetMarginPercent = settings.TargetMarginPercent;
        ViewBag.MinimumMarginPercent = settings.MinimumMarginPercent;
        ViewBag.RoundingStep = settings.RoundingStep;

        // E1: recibir un lote es lo que lo mete FÍSICAMENTE a una bodega. Sin bodegas
        // creadas no se puede recibir, así que la vista tiene que poder avisarlo.
        await FillReceiveInventoryAsync(product.ProductID);

        return View(product);
    }

    /// <summary>
    /// Confirma la recepción del lote. Además de activar el producto y fijar los países,
    /// desde E1 registra la ENTRADA REAL a una bodega (movimiento <c>Reception</c>) con su
    /// cantidad y su costo unitario, que es lo que después alimenta la liquidación de E3.
    ///
    /// Regla: un lote en <c>Shipped</c> (recepción de verdad) NO se puede confirmar sin
    /// bodega y sin cantidad. Un lote ya <c>Active</c> usa esta misma pantalla solo para
    /// gestionar países, así que ahí la recepción es opcional (permite registrar reposiciones).
    /// </summary>
    [HasPermission(Modules.ProductApprovals, Permissions.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Receive(
        Guid id,
        List<Guid> availableCountryIds,
        Guid? warehouseId,
        int? receiveQuantity,
        decimal? receiveUnitCost)
    {
        var product = await _context.Products
            .Include(p => p.ProductCountries)
            .FirstOrDefaultAsync(p => p.ProductID == id);

        if (product == null) return NotFound();

        if (product.ProductStatus != ProductStatus.Shipped && product.ProductStatus != ProductStatus.Active)
            return RedirectToAction(nameof(Index));

        // 🔒 CANDADO DE E2: un lote NO pasa a Active sin precio de venta fijado. Es la
        //    garantía de que Bean nunca venda al costo. Se valida en el POST porque
        //    deshabilitar el botón en la vista no es una medida de seguridad.
        if (product.ProductStatus == ProductStatus.Shipped && product.Price <= 0m)
        {
            TempData["PricingError"] =
                "Este lote no tiene precio de venta fijado. Fíjalo antes de activarlo: " +
                "sin PVP, Bean vendería al costo.";

            return RedirectToAction(nameof(Receive), new { id });
        }

        var esRecepcionInicial = product.ProductStatus == ProductStatus.Shipped;
        var quantity = receiveQuantity ?? 0;

        // ⚠️ El movimiento Reception SUMA al saldo. La migración AddWarehouseInventory ya creó
        //    StockItems con el stock declarado por el proveedor, así que si ese lote ya tiene
        //    saldo, exigir una cantidad aquí lo contaría DOS VECES. Por eso, cuando ya hay
        //    saldo, la cantidad pasa a ser opcional (se puede recibir "sin entrada nueva").
        var yaTieneSaldo = await _context.StockItems
            .AnyAsync(s => s.ProductID == product.ProductID && s.QuantityOnHand > 0);

        // 🔒 CANDADO DE E1: sin bodega seleccionada no se recibe. En la recepción inicial es
        //    obligatorio; en un lote ya activo solo se exige si se está registrando entrada.
        if (esRecepcionInicial || quantity != 0)
        {
            if (warehouseId is null || warehouseId == Guid.Empty)
            {
                TempData["ReceiveError"] =
                    "Selecciona la bodega donde entra el lote. Sin bodega no se puede recibir: " +
                    "el inventario se lleva por ubicación.";
                return RedirectToAction(nameof(Receive), new { id });
            }

            if (quantity < 0)
            {
                TempData["ReceiveError"] = "La cantidad recibida no puede ser negativa. Para restar, usa un ajuste en Inventario.";
                return RedirectToAction(nameof(Receive), new { id });
            }

            if (quantity == 0 && !yaTieneSaldo)
            {
                TempData["ReceiveError"] =
                    "Indica cuántas unidades entraron a la bodega: sin cantidad el lote quedaría activo con 0 disponibles.";
                return RedirectToAction(nameof(Receive), new { id });
            }

            var bodegaValida = await _context.Warehouses
                .AnyAsync(w => w.WarehouseID == warehouseId.Value && w.Status && w.IsActive);

            if (!bodegaValida)
            {
                TempData["ReceiveError"] = "La bodega seleccionada no existe o ya no está operativa.";
                return RedirectToAction(nameof(Receive), new { id });
            }
        }

        var usuario = User.Identity?.Name ?? "SYSTEM";

        // Todo junto: si la recepción falla, ni se activa el producto ni se tocan los países.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // 1. Entrada física a bodega. InventoryService detecta la transacción abierta y
            //    se suma a ella en vez de anidar otra.
            if (warehouseId is Guid bodega && quantity > 0)
            {
                // Sin costo explícito se usa el costo del proveedor vigente: es lo que Bean
                // debe por ese lote y lo que necesita E3 para liquidar.
                decimal? costo = receiveUnitCost.HasValue && receiveUnitCost.Value > 0m
                    ? receiveUnitCost.Value
                    : product.SupplierPrice;

                // Un costo 0 no aporta nada al libro mayor y ensuciaría la liquidación de E3.
                if (costo <= 0m) costo = null;

                await _inventory.MoveStockAsync(
                    product.ProductID,
                    bodega,
                    StockMovementType.Reception,
                    quantity,                                   // firmado: entra a la bodega
                    StockReference.Reception(product.ProductID),
                    reason: null,
                    unitCost: costo,
                    user: usuario);

                _logger.LogInformation(
                    "Recepción de {Cantidad} unidades del lote {ProductId} en la bodega {WarehouseId} " +
                    "por {Usuario} (costo unitario {Costo}).",
                    quantity, product.ProductID, bodega, usuario, costo);
            }

            // 2. Disponibilidad por país (actualización incremental: se marcan y desmarcan).
            foreach (var pc in product.ProductCountries)
            {
                pc.IsAvailable = availableCountryIds.Contains(pc.CountryID);
            }

            // 3. Activar el producto si venía de Shipped.
            if (product.ProductStatus == ProductStatus.Shipped)
            {
                product.ProductStatus = ProductStatus.Active;
            }

            product.UpdatedBy = usuario;
            product.UpdatedOn = DateTime.Now;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            if (quantity > 0)
            {
                TempData["ReceiveOk"] =
                    $"Recepción registrada: {quantity} unidades entraron a bodega. " +
                    $"Disponible del lote: {product.Stock}.";
            }
            else if (esRecepcionInicial)
            {
                TempData["ReceiveOk"] =
                    "Lote recibido y activado sin entrada nueva: ya tenía saldo en bodega. " +
                    $"Disponible: {product.Stock}.";
            }
            else
            {
                TempData["ReceiveOk"] = "Disponibilidad por país actualizada.";
            }
        }
        catch (InventoryException ex)
        {
            await transaction.RollbackAsync();
            TempData["ReceiveError"] = ex.Message;
            return RedirectToAction(nameof(Receive), new { id });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();

            _logger.LogError(ex,
                "Falló la recepción del lote {ProductId} en la bodega {WarehouseId}.",
                product.ProductID, warehouseId);

            TempData["ReceiveError"] =
                "No se pudo registrar la recepción. No se aplicó ningún cambio: vuelve a intentarlo.";
            return RedirectToAction(nameof(Receive), new { id });
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Publica en <c>ViewBag</c> las bodegas operativas y el saldo actual del lote en cada
    /// una, para que la pantalla de recepción sepa dónde está entrando el café.
    /// </summary>
    private async Task FillReceiveInventoryAsync(Guid productId)
    {
        var warehouses = await _context.Warehouses
            .Where(w => w.Status && w.IsActive)
            .OrderByDescending(w => w.IsDefault)
            .ThenBy(w => w.Code)
            .Select(w => new InventoryFilterOption
            {
                Id = w.WarehouseID,
                Label = w.Code + " · " + w.Name
            })
            .ToListAsync();

        var balances = await _context.StockItems
            .Where(s => s.ProductID == productId)
            .Select(s => new InventoryBalanceRowViewModel
            {
                WarehouseID = s.WarehouseID,
                Code = s.Warehouse != null ? s.Warehouse.Code : "—",
                Name = s.Warehouse != null ? s.Warehouse.Name : "—",
                City = s.Warehouse != null ? s.Warehouse.City : string.Empty,
                WarehouseIsActive = s.Warehouse != null && s.Warehouse.IsActive,
                HasStockItem = true,
                QuantityOnHand = s.QuantityOnHand,
                QuantityReserved = s.QuantityReserved,
                ReorderPoint = s.ReorderPoint,
                Ownership = s.Ownership
            })
            .ToListAsync();

        ViewBag.ReceiveWarehouses = warehouses;
        ViewBag.ReceiveBalances = balances;
    }
}

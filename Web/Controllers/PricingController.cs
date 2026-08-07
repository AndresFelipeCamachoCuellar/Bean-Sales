using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Web.Attributes;
using Web.Constants;
using Web.Data;
using Web.Models.Enums;
using Web.Models.ViewModels;
using Web.Services.Pricing;

namespace Web.Controllers;

/// <summary>
/// Back-office de precios de Bean (épica E2). Aquí vive el PVP: el costo lo pone el
/// proveedor en su propio formulario, pero el precio al que se vende y el margen son
/// información interna, protegida por el módulo de permisos <c>Pricing</c>.
/// </summary>
[Authorize]
public class PricingController : Controller
{
    private const int PageSize = PricingIndexViewModel.PageSizeValue;

    private readonly ApplicationDbContext _context;
    private readonly PricingService _pricing;
    private readonly ILogger<PricingController> _logger;

    public PricingController(
        ApplicationDbContext context,
        PricingService pricing,
        ILogger<PricingController> logger)
    {
        _context = context;
        _pricing = pricing;
        _logger = logger;
    }

    // ------------------------------------------------------------------- Index

    /// <summary>
    /// Bandeja de precios. Pestaña por defecto: "Márgenes por revisar"
    /// (<c>MarginAlert = true</c>), que es donde caen los lotes cuyo costo rompió el mínimo
    /// y los que todavía no tienen PVP.
    /// </summary>
    [HasPermission(Modules.Pricing, Permissions.Read)]
    public async Task<IActionResult> Index(string? tab, string? q, int page = 1)
    {
        if (page < 1) page = 1;

        var settings = await _pricing.GetSettingsAsync();

        var vm = new PricingIndexViewModel
        {
            Tab = string.Equals(tab, "todos", StringComparison.OrdinalIgnoreCase) ? "todos" : "alertas",
            Query = q,
            Page = page,
            TargetMarginPercent = settings.TargetMarginPercent,
            MinimumMarginPercent = settings.MinimumMarginPercent,
            RoundingStep = settings.RoundingStep
        };

        var baseQuery = _context.Products.Where(p => p.Status);

        vm.CountAll = await baseQuery.CountAsync();
        vm.CountAlerts = await baseQuery.CountAsync(p => p.MarginAlert);

        var query = baseQuery;
        if (vm.Tab == "alertas")
        {
            query = query.Where(p => p.MarginAlert);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(p =>
                p.Name.Contains(term) ||
                (p.Lot != null && p.Lot.Contains(term)) ||
                (p.Provider != null && p.Provider.Name.Contains(term)));
        }

        vm.TotalCount = await query.CountAsync();

        var totalPages = vm.TotalPages;
        if (page > totalPages) page = totalPages;
        vm.Page = page;

        vm.Rows = await query
            .OrderByDescending(p => p.MarginAlert)
            .ThenByDescending(p => p.CreatedOn)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(p => new PricingRowViewModel
            {
                ProductID = p.ProductID,
                Name = p.Name,
                ProviderName = p.Provider != null ? p.Provider.Name : null,
                Lot = p.Lot,
                ProductStatus = p.ProductStatus,
                SupplierPrice = p.SupplierPrice,
                Price = p.Price,
                MarginAlert = p.MarginAlert,
                PriceSetAt = p.PriceSetAt,
                PriceSetBy = p.PriceSetBy
            })
            .ToListAsync();

        return View(vm);
    }

    // ----------------------------------------------------------------- History

    /// <summary>Histórico de cambios de precio de un lote (HU-2.5).</summary>
    [HasPermission(Modules.Pricing, Permissions.Read)]
    public async Task<IActionResult> History(Guid id)
    {
        var product = await _context.Products
            .Include(p => p.Provider)
            .FirstOrDefaultAsync(p => p.ProductID == id);

        if (product == null) return NotFound();

        var settings = await _pricing.GetSettingsAsync();

        var vm = new PricingHistoryViewModel
        {
            ProductID = product.ProductID,
            ProductName = product.Name,
            ProviderName = product.Provider?.Name,
            SupplierPrice = product.SupplierPrice,
            Price = product.Price,
            MarginAlert = product.MarginAlert,
            TargetMarginPercent = settings.TargetMarginPercent,
            MinimumMarginPercent = settings.MinimumMarginPercent,
            RoundingStep = settings.RoundingStep,
            Changes = await _context.PriceChangeLogs
                .Where(l => l.ProductID == id)
                .OrderByDescending(l => l.ChangedOn)
                .Take(200)
                .ToListAsync()
        };

        return View(vm);
    }

    // ---------------------------------------------------------------- SetPrice

    /// <summary>
    /// Fija el PVP de un lote. Permitir un precio bajo el mínimo es una decisión de
    /// negocio (promociones, liquidaciones), pero exige MOTIVO: sin él no se guarda.
    /// </summary>
    [HasPermission(Modules.Pricing, Permissions.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPrice(Guid id, decimal price, string? reason, string? returnUrl)
    {
        var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductID == id);
        if (product == null) return NotFound();

        if (price <= 0m)
        {
            TempData["PricingError"] = "El precio de venta debe ser mayor a 0.";
            return Back(returnUrl);
        }

        var settings = await _pricing.GetSettingsAsync();
        var below = PricingCalculator.IsBelowMinimum(price, product.SupplierPrice, settings.MinimumMarginPercent);

        if (below && string.IsNullOrWhiteSpace(reason))
        {
            TempData["PricingError"] =
                $"Ese precio deja el margen por debajo del mínimo ({settings.MinimumMarginPercent:N0} %). " +
                "Escribe el motivo para poder guardarlo.";
            return Back(returnUrl);
        }

        var result = await _pricing.ApplySalePriceAsync(
            product, price, User.Identity?.Name ?? "SYSTEM", reason);

        product.UpdatedBy = User.Identity?.Name ?? "SYSTEM";
        product.UpdatedOn = DateTime.Now;

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "PVP del lote {ProductId} fijado en {Precio} por {Usuario}. Margen {Margen} %{Alerta}.",
            product.ProductID, price, User.Identity?.Name ?? "SYSTEM",
            result.MarginAfter.Percent, result.MarginAlert ? " (BAJO EL MÍNIMO)" : string.Empty);

        TempData["PricingOk"] = result.MarginAfter.IsCalculable
            ? $"Precio actualizado. Margen: {result.MarginAfter.Percent:N2} %."
            : "Precio actualizado.";

        return Back(returnUrl);
    }

    // ---------------------------------------------------------------- Settings

    /// <summary>
    /// Configuración de márgenes. Solo SuperAdmin: cambiar el mínimo mueve la alerta de
    /// TODO el catálogo, así que no se delega vía permiso granular.
    /// </summary>
    [Authorize(Roles = Roles.SuperAdmin)]
    [HttpGet]
    public async Task<IActionResult> Settings()
    {
        var settings = await _pricing.GetSettingsAsync();

        return View(new PricingSettingsViewModel
        {
            TargetMarginPercent = settings.TargetMarginPercent,
            MinimumMarginPercent = settings.MinimumMarginPercent,
            RoundingStep = settings.RoundingStep
        });
    }

    [Authorize(Roles = Roles.SuperAdmin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(PricingSettingsViewModel model)
    {
        if (model.MinimumMarginPercent > model.TargetMarginPercent)
        {
            ModelState.AddModelError(nameof(model.MinimumMarginPercent),
                "El mínimo no puede ser mayor que el objetivo: el objetivo es la meta, el mínimo es el piso.");
        }

        if (!ModelState.IsValid) return View(model);

        var settings = await _context.PricingSettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new Models.PricingSettings
            {
                PricingSettingsID = Models.PricingSettings.SingletonId,
                Status = true,
                CreatedBy = User.Identity?.Name ?? "SYSTEM",
                CreatedOn = DateTime.Now
            };
            _context.PricingSettings.Add(settings);
        }

        settings.TargetMarginPercent = model.TargetMarginPercent;
        settings.MinimumMarginPercent = model.MinimumMarginPercent;
        settings.RoundingStep = model.RoundingStep;
        settings.ModifiedBy = User.Identity?.Name ?? "SYSTEM";
        settings.ModifiedOn = DateTime.Now;

        await _context.SaveChangesAsync();

        // La configuración vive cacheada: si no se invalida, el catálogo seguiría
        // evaluando los márgenes contra los valores viejos hasta 5 minutos.
        _pricing.InvalidateSettingsCache();

        // Cambiar el umbral cambia quién está "por revisar": se repasa el catálogo entero.
        // Son decenas de filas, no millones: se hace en línea a propósito.
        var productos = await _context.Products.Where(p => p.Status).ToListAsync();
        var alertas = 0;
        foreach (var p in productos)
        {
            if (await _pricing.RefreshMarginAlertAsync(p)) alertas++;
        }
        await _context.SaveChangesAsync();

        model.RecalculatedAlerts = alertas;
        TempData["PricingOk"] =
            $"Configuración guardada. Lotes marcados para revisar: {alertas}.";

        return View(model);
    }

    // ----------------------------------------------------------------- Helpers

    private IActionResult Back(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction(nameof(Index));
    }
}

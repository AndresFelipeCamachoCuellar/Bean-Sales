using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Web.Attributes;
using Web.Constants;
using Web.Data;
using Web.Models;
using Web.Models.Enums;
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

    public ProductApprovalController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        ProductImageService images,
        PricingService pricing)
    {
        _context = context;
        _userManager = userManager;
        _images = images;
        _pricing = pricing;
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

        return View(product);
    }

    [HasPermission(Modules.ProductApprovals, Permissions.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Receive(Guid id, List<Guid> availableCountryIds)
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

        // 1. Update Countries Availability
        // This allows Incremental Updates: User checks new countries, unchecks old ones, etc.
        foreach (var pc in product.ProductCountries)
        {
            pc.IsAvailable = availableCountryIds.Contains(pc.CountryID);
        }

        // 2. Activate Product if not already
        if (product.ProductStatus == ProductStatus.Shipped)
        {
             product.ProductStatus = ProductStatus.Active;
        }
        
        product.UpdatedBy = User.Identity?.Name ?? "SYSTEM";
        product.UpdatedOn = DateTime.Now;

        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }
}

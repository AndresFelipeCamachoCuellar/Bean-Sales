using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Web.Attributes;
using Web.Constants;
using Web.Data;
using Web.Models;
using Web.Models.Enums;
using Web.Models.ViewModels;
using Web.Services;
using Web.Services.Media;
using Web.Services.Pricing;
using Web.Services.Tenancy;

namespace Web.Controllers;

/// <summary>
/// Catálogo del PROVEEDOR: cada empresa gestiona únicamente sus propios lotes.
///
/// El aislamiento multi-tenant ya no se escribe a mano en cada acción: lo aplica
/// <see cref="IProviderScope"/> (E5.2). Dos reglas al tocar este controlador:
///  · Para listar → <c>scope.ApplyTo(query)</c>.
///  · Para cargar por ID → <c>scope.SingleOwnedAsync(query, p =&gt; p.ProductID == id)</c>,
///    NUNCA <c>FirstOrDefaultAsync(p =&gt; p.ProductID == id)</c> a secas: eso es un IDOR.
/// </summary>
[Authorize]
public class ProductsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ProductImageService _images;
    private readonly PricingService _pricing;
    private readonly IPermissionService _permissions;
    private readonly IProviderScope _scope;

    public ProductsController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        ProductImageService images,
        PricingService pricing,
        IPermissionService permissions,
        IProviderScope scope)
    {
        _context = context;
        _userManager = userManager;
        _images = images;
        _pricing = pricing;
        _permissions = permissions;
        _scope = scope;
    }

    [HasPermission(Modules.Products, Permissions.Read)]
    public async Task<IActionResult> Index()
    {
        // Pantalla exclusiva del proveedor: el staff de Bean ve el catálogo completo desde
        // Aprobaciones / Precios / Inventario, no desde aquí.
        var scope = await _scope.RequireProviderAsync();
        if (scope == null) return Forbid();

        var products = await scope
            .ApplyTo(_context.Products)
            .Include(p => p.ProductCountries)
            .ThenInclude(pc => pc.Country)
            .Where(p => p.Status)
            .OrderByDescending(p => p.CreatedOn)
            .ToListAsync();

        return View(products);
    }

    [HasPermission(Modules.Products, Permissions.Create)]
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var model = new ProductViewModel();
        await LoadPricingContextAsync(model);
        await LoadCountries(model);
        return View(model);
    }

    [HasPermission(Modules.Products, Permissions.Create)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductViewModel model)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        var scope = await _scope.RequireProviderAsync();
        if (currentUser == null || scope == null) return Forbid();

        // 🔒 El permiso se resuelve SIEMPRE en el servidor: el valor que venga en el
        // formulario se descarta (se puede forjar desde DevTools).
        var canEditSalePrice = await CanEditSalePriceAsync(currentUser);
        model.CanEditSalePrice = canEditSalePrice;

        if (ModelState.IsValid)
        {
            var product = new Product
            {
                ProductID = Guid.NewGuid(),
                // El dueño lo pone el servidor desde la sesión, nunca el formulario.
                ProviderID = scope.CurrentProviderId!.Value,
                Name = model.Name,
                Description = model.Description,
                // Costo del proveedor. El PVP lo fija Bean después (candado de E2): un
                // borrador nace sin PVP y no puede llegar a Active sin él.
                SupplierPrice = model.SupplierPrice,
                Price = canEditSalePrice ? model.Price : 0m,
                Stock = model.Stock,
                // Portada denormalizada. La escribe ProductImageService cuando se sube la
                // primera foto; aquí solo se respeta lo que venga del formulario (una URL
                // externa pegada a mano sigue siendo válida como respaldo).
                ImageUrl = model.ImageUrl,
                Origin = model.Origin,
                Farm = model.Farm,
                Altitude = model.Altitude,
                Process = model.Process,
                Variety = model.Variety,
                Lot = model.Lot,
                RoastDate = model.RoastDate,
                TastingNotes = model.TastingNotes,
                Rating = model.Rating,
                ReviewCount = model.ReviewCount,
                ShippingWeightGrams = model.ShippingWeightGrams,
                LengthCm = model.LengthCm,
                WidthCm = model.WidthCm,
                HeightCm = model.HeightCm,
                ProductStatus = ProductStatus.Draft,
                Status = true,
                CreatedBy = User.Identity?.Name ?? "SYSTEM",
                CreatedOn = DateTime.Now
            };

            // Add Target Countries
            foreach (var countryId in model.SelectedCountryIds)
            {
                product.ProductCountries.Add(new ProductCountry
                {
                    ProductID = product.ProductID,
                    CountryID = countryId,
                    IsTargeted = true, // Provider wants this
                    IsAvailable = false // Not approved yet
                });
            }

            _context.Products.Add(product);

            // Deja la alerta de margen coherente desde el minuto cero (un borrador sin PVP
            // siempre queda marcado: es justo lo que tiene que ver la bandeja de Bean).
            await _pricing.RefreshMarginAlertAsync(product);

            await _context.SaveChangesAsync();

            // Las fotos necesitan un producto que exista (el public_id se firma con su
            // ProductID). Por eso, en vez de volver al listado, se lleva al proveedor
            // directo a la pantalla donde SÍ puede subirlas.
            TempData["Flash"] = "Borrador guardado. Ahora agrega las fotos del lote.";
            return RedirectToAction(nameof(Edit), new { id = product.ProductID });
        }

        await LoadPricingContextAsync(model);
        await LoadCountries(model);
        return View(model);
    }

    [HasPermission(Modules.Products, Permissions.Update)]
    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        var scope = await _scope.RequireProviderAsync();
        if (scope == null) return Forbid();

        // El filtro por proveedor lo pone SingleOwnedAsync: un lote de otra empresa devuelve
        // null y sale por NotFound, sin llegar a materializarse.
        var product = await scope.SingleOwnedAsync(
            _context.Products.Include(p => p.ProductCountries),
            p => p.ProductID == id);

        if (product == null) return NotFound();

        // Only allow edit if Draft or Rejected
        if (product.ProductStatus != ProductStatus.Draft && product.ProductStatus != ProductStatus.Rejected)
        {
            return RedirectToAction(nameof(Index)); // Or show error "Cannot edit submitted product"
        }

        var model = new ProductViewModel
        {
            ProductId = product.ProductID.ToString(),
            Name = product.Name,
            Description = product.Description,
            Price = product.Price,
            SupplierPrice = product.SupplierPrice,
            MarginAlert = product.MarginAlert,
            PriceSetAt = product.PriceSetAt,
            PriceSetBy = product.PriceSetBy,
            Stock = product.Stock,
            ImageUrl = product.ImageUrl,
            Origin = product.Origin,
            Farm = product.Farm,
            Altitude = product.Altitude,
            Process = product.Process,
            Variety = product.Variety,
            Lot = product.Lot,
            RoastDate = product.RoastDate,
            TastingNotes = product.TastingNotes,
            Rating = product.Rating,
            ReviewCount = product.ReviewCount,
            ShippingWeightGrams = product.ShippingWeightGrams,
            LengthCm = product.LengthCm,
            WidthCm = product.WidthCm,
            HeightCm = product.HeightCm,
            Status = product.ProductStatus,
            RejectionReason = product.RejectionReason,
            SelectedCountryIds = product.ProductCountries.Where(pc => pc.IsTargeted).Select(pc => pc.CountryID).ToList()
        };

        // Gestor de fotos: el proveedor puede editarlas mientras el lote sea suyo y esté
        // en Borrador o Rechazado (ya validado arriba). Después las gestiona Bean.
        model.ImageManager = await _images.BuildManagerAsync(product, canEdit: true, role: ImageUploader.Provider);

        await LoadPricingContextAsync(model);
        await LoadCountries(model);
        return View(model);
    }

    [HasPermission(Modules.Products, Permissions.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ProductViewModel model)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        var scope = await _scope.RequireProviderAsync();
        if (currentUser == null || scope == null) return Forbid();

        // 🔒 Igual que en Create: el permiso se resuelve en el servidor, nunca desde el POST.
        var canEditSalePrice = await CanEditSalePriceAsync(currentUser);
        model.CanEditSalePrice = canEditSalePrice;

        // El id llega como string desde el formulario: se parsea AQUÍ, no dentro de la
        // consulta. Comparar p.ProductID.ToString() obligaba a SQL Server a convertir cada
        // fila (CAST) y a descartar el índice de la clave primaria.
        if (!Guid.TryParse(model.ProductId, out var editingId))
        {
            return NotFound();
        }

        if (ModelState.IsValid)
        {
            // 🔒 El POST revalida EXACTAMENTE lo mismo que filtró el GET. Confiar en que el
            // formulario solo pueda traer ids ya vistos es confiar en el navegador.
            var product = await scope.SingleOwnedAsync(
                _context.Products.Include(p => p.ProductCountries),
                p => p.ProductID == editingId);

            if (product == null) return NotFound();

            if (product.ProductStatus != ProductStatus.Draft && product.ProductStatus != ProductStatus.Rejected)
            {
                 ModelState.AddModelError("", "No se puede editar un producto que está en proceso de aprobación o ya aprobado.");
                 model.ImageManager = await _images.BuildManagerAsync(product, canEdit: false, role: ImageUploader.Provider);
                 await LoadPricingContextAsync(model);
                 await LoadCountries(model);
                 return View(model);
            }

            product.Name = model.Name;
            product.Description = model.Description;
            product.Stock = model.Stock;

            // --- Precios ------------------------------------------------------------
            // El COSTO lo mueve el proveedor: el PVP NO se toca y, si el margen cae bajo
            // el mínimo, se enciende MarginAlert (el producto SIGUE vendiéndose).
            await _pricing.ApplySupplierPriceAsync(
                product, model.SupplierPrice, User.Identity?.Name ?? "SYSTEM");

            // El PVP solo se acepta de quien tiene Pricing/Update. Sin el permiso, el valor
            // que llegue en el POST se ignora por completo (no basta con no renderizarlo).
            if (canEditSalePrice && product.Price != model.Price)
            {
                await _pricing.ApplySalePriceAsync(
                    product, model.Price, User.Identity?.Name ?? "SYSTEM", model.SalePriceReason);
            }

            // ⚠️ ImageUrl es la PORTADA denormalizada que mantiene ProductImageService.
            // El formulario del producto no la expone, así que model.ImageUrl llega null:
            // sobrescribirla borraría la portada recién sincronizada. Solo se toma del
            // formulario cuando el lote NO tiene fotos en la galería (compatibilidad con
            // las URLs externas que se pegaban a mano antes de este incremento).
            if (await _images.CountAsync(product.ProductID) == 0)
            {
                product.ImageUrl = model.ImageUrl;
            }

            product.Origin = model.Origin;
            product.Farm = model.Farm;
            product.Altitude = model.Altitude;
            product.Process = model.Process;
            product.Variety = model.Variety;
            product.Lot = model.Lot;
            product.RoastDate = model.RoastDate;
            product.TastingNotes = model.TastingNotes;
            product.Rating = model.Rating;
            product.ReviewCount = model.ReviewCount;
            product.ShippingWeightGrams = model.ShippingWeightGrams;
            product.LengthCm = model.LengthCm;
            product.WidthCm = model.WidthCm;
            product.HeightCm = model.HeightCm;

            // If it was Rejected, reset to Draft on edit? Usually yes, forcing user to resubmit.
            if (product.ProductStatus == ProductStatus.Rejected)
            {
                product.ProductStatus = ProductStatus.Draft;
                product.RejectionReason = null;
            }

            product.UpdatedBy = User.Identity?.Name ?? "SYSTEM";
            product.UpdatedOn = DateTime.Now;

            // Update Countries
            // Remove existing
            var countriesToRemove = product.ProductCountries.ToList();
            _context.ProductCountries.RemoveRange(countriesToRemove);
            
            // Add new
            foreach (var countryId in model.SelectedCountryIds)
            {
                _context.ProductCountries.Add(new ProductCountry
                {
                    ProductID = product.ProductID,
                    CountryID = countryId,
                    IsTargeted = true,
                    IsAvailable = false
                });
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // ModelState inválido: se rearma el gestor de fotos para que el proveedor no
        // pierda de vista lo que ya subió (las fotos NO viven en este formulario).
        var editing = await scope.SingleOwnedAsync(
            _context.Products,
            p => p.ProductID == editingId);

        if (editing is not null)
        {
            model.ImageManager = await _images.BuildManagerAsync(editing, canEdit: true, role: ImageUploader.Provider);
        }

        await LoadPricingContextAsync(model);
        await LoadCountries(model);
        return View(model);
    }

    [HasPermission(Modules.Products, Permissions.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitForApproval(Guid id)
    {
        var scope = await _scope.RequireProviderAsync();
        if (scope == null) return Forbid();

        var product = await scope.SingleOwnedAsync(_context.Products, p => p.ProductID == id);
        if (product == null) return NotFound();

        if (product.ProductStatus == ProductStatus.Draft)
        {
            product.ProductStatus = ProductStatus.PendingApproval;
            product.UpdatedBy = User.Identity?.Name ?? "SYSTEM";
            product.UpdatedOn = DateTime.Now;
            await _context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    [HasPermission(Modules.Products, Permissions.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ship(Guid id, string shippingDetails)
    {
        var scope = await _scope.RequireProviderAsync();
        if (scope == null) return Forbid();

        var product = await scope.SingleOwnedAsync(_context.Products, p => p.ProductID == id);
        if (product == null) return NotFound();

        // Ensure product is in correct state
        if (product.ProductStatus == ProductStatus.ApprovedToShip)
        {
            product.ProductStatus = ProductStatus.Shipped;
            product.ShippingDetails = shippingDetails;
            product.UpdatedBy = User.Identity?.Name ?? "SYSTEM";
            product.UpdatedOn = DateTime.Now;
            await _context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }
    
    // ------------------------------------------------------------------ Helpers

    /// <summary>
    /// ¿Este usuario puede fijar el PVP? Es la única fuente de verdad: ni la vista ni el
    /// formulario deciden esto.
    /// </summary>
    private Task<bool> CanEditSalePriceAsync(ApplicationUser user) =>
        _permissions.HasPermissionAsync(user, Modules.Pricing, Permissions.Update);

    /// <summary>
    /// Carga en el ViewModel lo que la vista necesita para pintar (o esconder) el bloque de
    /// PVP y el panel de margen.
    /// </summary>
    private async Task LoadPricingContextAsync(ProductViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        model.CanEditSalePrice = user != null && await CanEditSalePriceAsync(user);

        var settings = await _pricing.GetSettingsAsync();
        model.TargetMarginPercent = settings.TargetMarginPercent;
        model.MinimumMarginPercent = settings.MinimumMarginPercent;
        model.RoundingStep = settings.RoundingStep;
    }

    private async Task LoadCountries(ProductViewModel model)
    {
        var countries = await _context.Countries.Where(c => c.Status).ToListAsync();
        model.AvailableCountries = countries.Select(c => new SelectListItem
        {
            Value = c.CountryID.ToString(),
            Text = c.Name
        });
    }
}

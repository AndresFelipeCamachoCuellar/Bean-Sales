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
using Web.Services.Media;

namespace Web.Controllers;

[Authorize]
public class ProductsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ProductImageService _images;

    public ProductsController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        ProductImageService images)
    {
        _context = context;
        _userManager = userManager;
        _images = images;
    }

    [HasPermission(Modules.Products, Permissions.Read)]
    public async Task<IActionResult> Index()
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.ProviderID == null) return Forbid();

        var products = await _context.Products
            .Include(p => p.ProductCountries)
            .ThenInclude(pc => pc.Country)
            .Where(p => p.ProviderID == currentUser.ProviderID && p.Status)
            .OrderByDescending(p => p.CreatedOn)
            .ToListAsync();

        return View(products);
    }

    [HasPermission(Modules.Products, Permissions.Create)]
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var model = new ProductViewModel();
        await LoadCountries(model);
        return View(model);
    }

    [HasPermission(Modules.Products, Permissions.Create)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductViewModel model)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.ProviderID == null) return Forbid();

        if (ModelState.IsValid)
        {
            var product = new Product
            {
                ProductID = Guid.NewGuid(),
                ProviderID = currentUser.ProviderID.Value,
                Name = model.Name,
                Description = model.Description,
                Price = model.Price,
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
            await _context.SaveChangesAsync();

            // Las fotos necesitan un producto que exista (el public_id se firma con su
            // ProductID). Por eso, en vez de volver al listado, se lleva al proveedor
            // directo a la pantalla donde SÍ puede subirlas.
            TempData["Flash"] = "Borrador guardado. Ahora agrega las fotos del lote.";
            return RedirectToAction(nameof(Edit), new { id = product.ProductID });
        }

        await LoadCountries(model);
        return View(model);
    }

    [HasPermission(Modules.Products, Permissions.Update)]
    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.ProviderID == null) return Forbid();

        var product = await _context.Products
            .Include(p => p.ProductCountries)
            .FirstOrDefaultAsync(p => p.ProductID == id && p.ProviderID == currentUser.ProviderID);

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

        await LoadCountries(model);
        return View(model);
    }

    [HasPermission(Modules.Products, Permissions.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ProductViewModel model)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.ProviderID == null) return Forbid();

        if (ModelState.IsValid)
        {
            var product = await _context.Products
                .Include(p => p.ProductCountries)
                .FirstOrDefaultAsync(p => p.ProductID.ToString() == model.ProductId && p.ProviderID == currentUser.ProviderID);

            if (product == null) return NotFound();

            if (product.ProductStatus != ProductStatus.Draft && product.ProductStatus != ProductStatus.Rejected)
            {
                 ModelState.AddModelError("", "No se puede editar un producto que está en proceso de aprobación o ya aprobado.");
                 model.ImageManager = await _images.BuildManagerAsync(product, canEdit: false, role: ImageUploader.Provider);
                 await LoadCountries(model);
                 return View(model);
            }

            product.Name = model.Name;
            product.Description = model.Description;
            product.Price = model.Price;
            product.Stock = model.Stock;

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
        if (Guid.TryParse(model.ProductId, out var editingId))
        {
            var editing = await _context.Products
                .FirstOrDefaultAsync(p => p.ProductID == editingId && p.ProviderID == currentUser.ProviderID);

            if (editing is not null)
            {
                model.ImageManager = await _images.BuildManagerAsync(editing, canEdit: true, role: ImageUploader.Provider);
            }
        }

        await LoadCountries(model);
        return View(model);
    }

    [HasPermission(Modules.Products, Permissions.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitForApproval(Guid id)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.ProviderID == null) return Forbid();

        var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductID == id && p.ProviderID == currentUser.ProviderID);
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
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.ProviderID == null) return Forbid();

        var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductID == id && p.ProviderID == currentUser.ProviderID);
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
    
    // Helper
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

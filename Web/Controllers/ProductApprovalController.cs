using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Web.Attributes;
using Web.Constants;
using Web.Data;
using Web.Models;
using Web.Models.Enums;

namespace Web.Controllers;

[Authorize]
public class ProductApprovalController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public ProductApprovalController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
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
            .Where(p => statusesOfInterest.Contains(p.ProductStatus) && p.Status)
            .OrderBy(p => p.ProductStatus)
            .ThenByDescending(p => p.CreatedOn)
            .ToListAsync();

        return View(products);
    }

    // ... Approve/Reject ...

    [HasPermission(Modules.ProductApprovals, Permissions.Update)]
    [HttpGet]
    public async Task<IActionResult> Receive(Guid id)
    {
        var product = await _context.Products
            .Include(p => p.ProductCountries)
            .ThenInclude(pc => pc.Country)
            .FirstOrDefaultAsync(p => p.ProductID == id);

        if (product == null) return NotFound();

        // Allow Shipped OR Active (to update countries)
        if (product.ProductStatus != ProductStatus.Shipped && product.ProductStatus != ProductStatus.Active)
        {
            return RedirectToAction(nameof(Index));
        }

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

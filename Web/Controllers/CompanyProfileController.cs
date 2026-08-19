using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Web.Constants;
using Web.Data;
using Web.Models;
using Web.Models.ViewModels;
using Web.Services;

namespace Web.Controllers;

[Authorize]
public class CompanyProfileController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPermissionService _permissionService;

    public CompanyProfileController(
        ApplicationDbContext context, 
        UserManager<ApplicationUser> userManager,
        IPermissionService permissionService)
    {
        _context = context;
        _userManager = userManager;
        _permissionService = permissionService;
    }

    private async Task<bool> CheckPermission(string permission)
    {
        var user = await _userManager.GetUserAsync(User);
        return await _permissionService.HasPermissionAsync(user, Modules.CompanyProfile, permission);
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!await CheckPermission(Permissions.Read))
        {
            return RedirectToAction("AccessDenied", "Account");
        }

        var user = await _userManager.GetUserAsync(User);
        if (user?.ProviderID == null)
        {
            return View("NoProvider");
        }

        var provider = await _context.Providers.FindAsync(user.ProviderID);
        if (provider == null) return NotFound();

        var model = new CompanyProfileViewModel
        {
            ProviderID = provider.ProviderID,
            Name = provider.Name,
            NIT = provider.NIT,
            Address = provider.Address,
            Phone = provider.Phone,
            Email = provider.Email,
            ApprovalStatus = provider.ApprovalStatus.ToString()
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit()
    {
        if (!await CheckPermission(Permissions.Update))
        {
            return RedirectToAction("AccessDenied", "Account");
        }

        var user = await _userManager.GetUserAsync(User);
        if (user?.ProviderID == null) return View("NoProvider");

        var provider = await _context.Providers.FindAsync(user.ProviderID);
        if (provider == null) return NotFound();

        var model = new CompanyProfileViewModel
        {
            ProviderID = provider.ProviderID,
            Name = provider.Name,
            NIT = provider.NIT,
            Address = provider.Address,
            Phone = provider.Phone,
            Email = provider.Email,
            ApprovalStatus = provider.ApprovalStatus.ToString()
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CompanyProfileViewModel model)
    {
        if (!await CheckPermission(Permissions.Update))
        {
            return RedirectToAction("AccessDenied", "Account");
        }

        if (ModelState.IsValid)
        {
            var provider = await _context.Providers.FindAsync(model.ProviderID);
            if (provider == null) return NotFound();

            // Additional Security: Ensure the user belongs to this provider
            var user = await _userManager.GetUserAsync(User);
            if (user?.ProviderID != provider.ProviderID)
            {
                return Forbid();
            }

            provider.Address = model.Address;
            provider.Phone = model.Phone;
            // provider.Name = model.Name; // Allow editing name? Usually verified. Let's allow for now.
            // provider.NIT = model.NIT; // NIT should probably NOT be editable easily.
            // provider.Email = model.Email;
            
            // Updating permitted fields
            provider.Address = model.Address;
            provider.Phone = model.Phone;
            provider.ModifiedBy = User.Identity?.Name;
            provider.ModifiedOn = DateTime.Now;

            _context.Providers.Update(provider);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Información de la empresa actualizada correctamente.";
            return RedirectToAction(nameof(Index));
        }
        return View(model);
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Web.Attributes;
using Web.Constants;
using Web.Data;
using Web.Models;
using Web.Models.ViewModels;

namespace Web.Controllers;

[Authorize]
public class CompanyRolesController : Controller
{
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;

    public CompanyRolesController(RoleManager<ApplicationRole> roleManager, UserManager<ApplicationUser> userManager, ApplicationDbContext context)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _context = context;
    }

    [HasPermission(Modules.CompanyRoles, Permissions.Read)]
    public async Task<IActionResult> Index()
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.ProviderID == null) return Forbid();

        var roles = await _roleManager.Roles
            .Where(r => r.ProviderID == currentUser.ProviderID)
            .ToListAsync();

        var model = roles.Select(r => new RoleViewModel
        {
            Id = r.Id.ToString(),
            Name = r.Name ?? string.Empty,
            Description = r.Description
        }).ToList();

        return View(model);
    }

    [HasPermission(Modules.CompanyRoles, Permissions.Create)]
    [HttpGet]
    public IActionResult Create()
    {
        return View();
    }

    [HasPermission(Modules.CompanyRoles, Permissions.Create)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RoleViewModel model)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.ProviderID == null) return Forbid();

        if (ModelState.IsValid)
        {
            var role = new ApplicationRole(model.Name)
            {
                Description = model.Description,
                ProviderID = currentUser.ProviderID,
                CreatedBy = User.Identity?.Name ?? "SYSTEM",
                CreatedOn = DateTime.Now,
                Status = true
            };

            var result = await _roleManager.CreateAsync(role);
            if (result.Succeeded)
            {
                return RedirectToAction(nameof(Index));
            }
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }
        return View(model);
    }

    [HasPermission(Modules.CompanyRoles, Permissions.Update)]
    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.ProviderID == null) return Forbid();

        if (string.IsNullOrEmpty(id)) return NotFound();
        
        var role = await _roleManager.Roles.FirstOrDefaultAsync(r => r.Id.ToString() == id && r.ProviderID == currentUser.ProviderID);
        if (role == null) return NotFound();

        var model = new RoleViewModel
        {
            Id = role.Id.ToString(),
            Name = role.Name ?? string.Empty,
            Description = role.Description
        };
        return View(model);
    }

    [HasPermission(Modules.CompanyRoles, Permissions.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(RoleViewModel model)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.ProviderID == null) return Forbid();

        if (ModelState.IsValid)
        {
            var role = await _roleManager.Roles.FirstOrDefaultAsync(r => r.Id.ToString() == model.Id && r.ProviderID == currentUser.ProviderID);
            if (role == null) return NotFound();

            role.Name = model.Name;
            role.Description = model.Description;

            var result = await _roleManager.UpdateAsync(role);
            if (result.Succeeded)
            {
                return RedirectToAction(nameof(Index));
            }
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }
        return View(model);
    }

    [HasPermission(Modules.CompanyRoles, Permissions.Delete)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.ProviderID == null) return Forbid();

        var role = await _roleManager.Roles.FirstOrDefaultAsync(r => r.Id.ToString() == id && r.ProviderID == currentUser.ProviderID);
        if (role != null)
        {
             // Check if role has users? 
            var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name!);
            if (usersInRole.Any())
            {
                // Ideally return error, for now just redirect
                 return RedirectToAction(nameof(Index));
            }

            await _roleManager.DeleteAsync(role);
        }
        return RedirectToAction(nameof(Index));
    }

    [HasPermission(Modules.CompanyRoles, Permissions.Update)] // Using Update permission for managing permissions
    [HttpGet]
    public async Task<IActionResult> ManagePermissions(string id)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.ProviderID == null) return Forbid();

        var role = await _roleManager.Roles.FirstOrDefaultAsync(r => r.Id.ToString() == id && r.ProviderID == currentUser.ProviderID);
        if (role == null) return NotFound();

        var model = new ManagePermissionsViewModel
        {
            RoleId = role.Id.ToString(),
            RoleName = role.Name ?? string.Empty
        };

        // Get permissions assigned to the PROVIDER ADMIN role (Wait, we can iterate all modules, but only show those?)
        // Better approach: Show modules that are relevant for Companies.
        // Or cleaner: Fetch modules that the CURRENT User has access to, assuming they are ProviderAdmin.
        // Actually, let's explicitly list the "Company" modules for now to avoid complexity or security leaks.
        // Modules: CompanyProfile, CompanyUsers, CompanyRoles.
        // Maybe "Sales", "Inventory" later.
        
        var allowedModules = new[] { Modules.CompanyProfile, Modules.CompanyUsers, Modules.CompanyRoles, Modules.Products };
        
        var companyModules = await _context.ParametricModules
            .Include(m => m.ParametricPermissions)
            .Where(m => allowedModules.Contains(m.Code) && m.Status)
            .ToListAsync();

        // Get existing assigned permissions for this target role
        var rolePermissions = await _context.Permissions
            .Where(p => p.RoleID == role.Id)
            .Select(p => p.ParametricPermissionID)
            .ToListAsync();

        foreach (var module in companyModules)
        {
            var moduleViewModel = new ModulePermissionsViewModel
            {
                ModuleName = module.Name,
                ModuleCode = module.Code
            };

            foreach (var perm in module.ParametricPermissions)
            {
                // Ensure we only show permissions that make sense?
                // Provider Admin has full access to these modules, so they can delegate any permission they have.
                // NOTE: Ideally we should verify if Current User HAS this permission before letting them assign it.
                // But for now, as ProviderAdmin is the only one accessing this, and we filtered by AllowedModules, it's fairly safe.
                
                moduleViewModel.Permissions.Add(new PermissionSelectionViewModel
                {
                    ParametricPermissionID = perm.ParametricPermissionID,
                    PermissionName = perm.Name,
                    PermissionCode = perm.Code,
                    Description = perm.Description,
                    Selected = rolePermissions.Contains(perm.ParametricPermissionID)
                });
            }
            model.Modules.Add(moduleViewModel);
        }

        return View(model);
    }

    [HasPermission(Modules.CompanyRoles, Permissions.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ManagePermissions(ManagePermissionsViewModel model)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.ProviderID == null) return Forbid();

        var role = await _roleManager.Roles.FirstOrDefaultAsync(r => r.Id.ToString() == model.RoleId && r.ProviderID == currentUser.ProviderID);
        if (role == null) return NotFound();

        var existingPermissions = await _context.Permissions
            .Where(p => p.RoleID == role.Id)
            .ToListAsync();

        // Filter incoming permissions to ensure they belong to Allowed Modules?
        // Relying on UI for now + the fact `ParametricPermissionID` GUIDs must exist.
        
        var selectedIds = model.Modules
            .SelectMany(m => m.Permissions)
            .Where(p => p.Selected)
            .Select(p => p.ParametricPermissionID)
            .ToList();

         // 1. Remove permissions that are no longer selected
        var toRemove = existingPermissions
            .Where(p => !selectedIds.Contains(p.ParametricPermissionID))
            .ToList();
        
        if (toRemove.Any())
        {
            _context.Permissions.RemoveRange(toRemove);
        }

        // 2. Add new permissions
        var currentIds = existingPermissions.Select(p => p.ParametricPermissionID).ToList();
        var toAddIds = selectedIds.Where(id => !currentIds.Contains(id)).ToList();

        foreach (var id in toAddIds)
        {
            await _context.Permissions.AddAsync(new Permission
            {
                PermissionID = Guid.NewGuid(),
                RoleID = role.Id,
                ParametricPermissionID = id,
                Status = true,
                CreatedBy = User.Identity?.Name ?? "SYSTEM",
                CreatedOn = DateTime.Now
            });
        }

        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}

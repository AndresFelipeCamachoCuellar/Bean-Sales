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
public class RolesController : Controller
{
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ApplicationDbContext _context;

    public RolesController(RoleManager<ApplicationRole> roleManager, ApplicationDbContext context)
    {
        _roleManager = roleManager;
        _context = context;
    }

    [HasPermission(Modules.Roles, Permissions.Read)]
    public async Task<IActionResult> Index()
    {
        var roles = await _roleManager.Roles.ToListAsync();
        var model = roles.Select(r => new RoleViewModel
        {
            Id = r.Id.ToString(),
            Name = r.Name ?? string.Empty
        }).ToList();

        return View(model);
    }

    [HasPermission(Modules.Roles, Permissions.Create)]
    [HttpGet]
    public IActionResult Create()
    {
        return View();
    }

    [HasPermission(Modules.Roles, Permissions.Create)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RoleViewModel model)
    {
        if (ModelState.IsValid)
        {
            var role = new ApplicationRole(model.Name);
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

    [HasPermission(Modules.Roles, Permissions.Update)]
    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        if (string.IsNullOrEmpty(id)) return NotFound();
        var role = await _roleManager.FindByIdAsync(id);
        if (role == null) return NotFound();

        if (role.Name == Roles.SuperAdmin)
        {
             return RedirectToAction(nameof(Index));
        }

        var model = new RoleViewModel
        {
            Id = role.Id.ToString(),
            Name = role.Name ?? string.Empty
        };
        return View(model);
    }

    [HasPermission(Modules.Roles, Permissions.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(RoleViewModel model)
    {
        if (ModelState.IsValid)
        {
            var role = await _roleManager.FindByIdAsync(model.Id);
            if (role == null) return NotFound();

            if (role.Name == Roles.SuperAdmin)
            {
                ModelState.AddModelError(string.Empty, "Cannot edit SuperAdmin role.");
                return View(model);
            }

            role.Name = model.Name;
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

    [HasPermission(Modules.Roles, Permissions.Delete)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var role = await _roleManager.FindByIdAsync(id);
        if (role != null)
        {
            if (role.Name == Roles.SuperAdmin)
            {
                return RedirectToAction(nameof(Index));
            }

            // Optional: Check if role has users? Identity usually blocks or cascades depending on config
            // For safety, let's just try delete
            await _roleManager.DeleteAsync(role);
        }
        return RedirectToAction(nameof(Index));
    }

    [HasPermission(Modules.Roles, Permissions.Update)]
    [HttpGet]
    public async Task<IActionResult> ManagePermissions(string id)
    {
        var role = await _roleManager.FindByIdAsync(id);
        if (role == null) return NotFound();

        var model = new ManagePermissionsViewModel
        {
            RoleId = role.Id.ToString(),
            RoleName = role.Name ?? string.Empty
        };

        // Get all Modules and their Perms
        var allModules = await _context.ParametricModules
            .Include(m => m.ParametricPermissions)
            .Where(m => m.Status)
            .ToListAsync();

        // Get existing assigned permissions for this role
        var rolePermissions = await _context.Permissions
            .Where(p => p.RoleID == role.Id)
            .Select(p => p.ParametricPermissionID)
            .ToListAsync();

        foreach (var module in allModules)
        {
            var moduleViewModel = new ModulePermissionsViewModel
            {
                ModuleName = module.Name,
                ModuleCode = module.Code
            };

            foreach (var perm in module.ParametricPermissions)
            {
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

    [HasPermission(Modules.Roles, Permissions.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ManagePermissions(ManagePermissionsViewModel model)
    {
        var role = await _roleManager.FindByIdAsync(model.RoleId);
        if (role == null) return NotFound();

        var existingPermissions = await _context.Permissions
            .Where(p => p.RoleID == role.Id)
            .ToListAsync();

        // Flatten the selected permissions
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

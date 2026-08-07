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
    /// <summary>
    /// 🔒 ÚNICA fuente de verdad de qué módulos puede delegar un proveedor dentro de su
    /// propia empresa. La usan el GET (para pintar) y el POST (para AUTORIZAR).
    ///
    /// NO agregar aquí módulos internos de Bean: <c>Pricing</c>, <c>Agreements</c>,
    /// <c>Settlements</c>, <c>Orders</c>, <c>Users</c>, <c>Roles</c> exponen costo,
    /// margen y condiciones comerciales del negocio, no del proveedor.
    /// </summary>
    private static readonly string[] AllowedModuleCodes =
    {
        Modules.CompanyProfile,
        Modules.CompanyUsers,
        Modules.CompanyRoles,
        Modules.Products
    };

    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<CompanyRolesController> _logger;

    public CompanyRolesController(
        RoleManager<ApplicationRole> roleManager,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context,
        ILogger<CompanyRolesController> logger)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _context = context;
        _logger = logger;
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

        return View(await BuildManagePermissionsModelAsync(role));
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

        var selectedIds = model.Modules
            .SelectMany(m => m.Permissions)
            .Where(p => p.Selected)
            .Select(p => p.ParametricPermissionID)
            .Distinct()
            .ToList();

        // 🔒 REVALIDACIÓN SERVER-SIDE. El formulario postea GUIDs de ParametricPermission;
        // sin este filtro, un ProviderAdmin que forje IDs podía asignarse permisos de
        // CUALQUIER módulo (Pricing, Agreements, Settlements, Orders, Users…). El GET solo
        // pinta los módulos permitidos, pero eso es UI: la autorización se decide aquí.
        var allowedPermissionIds = (await LoadAllowedModulesAsync())
            .SelectMany(m => m.ParametricPermissions ?? new List<ParametricPermission>())
            .Select(p => p.ParametricPermissionID)
            .ToHashSet();

        var forgedIds = selectedIds.Where(id => !allowedPermissionIds.Contains(id)).ToList();
        if (forgedIds.Count > 0)
        {
            // No se ignora en silencio: se registra y se rechaza la operación completa.
            _logger.LogWarning(
                "Intento de asignar {Count} permiso(s) fuera de los módulos permitidos en el rol {RoleId} " +
                "del proveedor {ProviderId}, por el usuario {User}. IDs: {Ids}.",
                forgedIds.Count, role.Id, currentUser.ProviderID, User.Identity?.Name ?? "desconocido",
                string.Join(", ", forgedIds));

            ModelState.AddModelError(string.Empty,
                "Algunos permisos enviados no pertenecen a los módulos que puedes administrar. " +
                "No se guardó ningún cambio.");

            return View(await BuildManagePermissionsModelAsync(role));
        }

        // 1. Quitar los que se destildaron. Se limita a los módulos administrables: un
        //    permiso que Bean le haya concedido a este rol fuera de la lista (p. ej.
        //    Orders/Read) NO se puede borrar desde esta pantalla, porque tampoco se ve.
        var toRemove = existingPermissions
            .Where(p => allowedPermissionIds.Contains(p.ParametricPermissionID)
                        && !selectedIds.Contains(p.ParametricPermissionID))
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

    // ------------------------------------------------------------------ Helpers

    /// <summary>
    /// Módulos administrables por un proveedor, con sus permisos. La comparten el GET
    /// (pintar) y el POST (autorizar): una sola consulta, una sola verdad.
    /// </summary>
    private async Task<List<ParametricModule>> LoadAllowedModulesAsync()
    {
        return await _context.ParametricModules
            .Include(m => m.ParametricPermissions)
            .Where(m => AllowedModuleCodes.Contains(m.Code) && m.Status)
            .ToListAsync();
    }

    /// <summary>Arma la matriz de permisos del rol indicado (GET y re-render del POST fallido).</summary>
    private async Task<ManagePermissionsViewModel> BuildManagePermissionsModelAsync(ApplicationRole role)
    {
        var model = new ManagePermissionsViewModel
        {
            RoleId = role.Id.ToString(),
            RoleName = role.Name ?? string.Empty
        };

        var companyModules = await LoadAllowedModulesAsync();

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

            foreach (var perm in module.ParametricPermissions ?? new List<ParametricPermission>())
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

        return model;
    }
}

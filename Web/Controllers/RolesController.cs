using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Web.Attributes;
using Web.Constants;
using Web.Data;
using Web.Models;
using Web.Models.ViewModels;
using Web.Services.Tenancy;

namespace Web.Controllers;

/// <summary>
/// Roles GLOBALES de Bean (<c>ProviderID == null</c>).
///
/// ─────────────────────────────────────────────────────────────────────────────────────
/// 🔒 ALCANCE: SOLO ROLES DE BEAN
/// ─────────────────────────────────────────────────────────────────────────────────────
/// Antes esta pantalla listaba los roles de TODAS las empresas proveedoras (mostrando cómo
/// organiza su equipo cada una) y <c>Edit</c>, <c>Delete</c> y <c>ManagePermissions</c>
/// cargaban por ID sin comprobar de quién era el rol: con el ID a mano se podía renombrar,
/// borrar o repermisar un rol de otro tenant (IDOR clásico).
///
/// Ahora TODA lectura pasa por <see cref="BeanRoles"/> y toda carga por ID por
/// <see cref="LoadBeanRoleAsync"/>, que filtran <c>ProviderID == null</c>. Un rol de
/// proveedor devuelve <c>NotFound()</c> desde aquí — se gestiona en <c>CompanyRoles</c>,
/// que a su vez solo ve los del proveedor del usuario.
/// </summary>
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

    /// <summary>Roles de Bean. Es el único punto de entrada permitido a la tabla de roles.</summary>
    private IQueryable<ApplicationRole> BeanRoles => _roleManager.Roles.Where(r => r.ProviderID == null);

    /// <summary>
    /// Carga un rol de Bean por ID comprobando la pertenencia EN LA MISMA consulta. Si el ID
    /// es de otro tenant (o no es un GUID) devuelve <c>null</c> y el llamador responde
    /// <c>NotFound()</c>: nunca se materializa la fila ajena.
    /// </summary>
    private async Task<ApplicationRole?> LoadBeanRoleAsync(string? id)
    {
        if (!Guid.TryParse(id, out var roleId)) return null;

        // Comparación por Guid, no por Id.ToString(): un CAST por fila descarta el índice de PK.
        return await BeanRoles.FirstOrDefaultAsync(r => r.Id == roleId);
    }

    /// <summary>Roles que el sistema necesita por NOMBRE y por tanto no se renombran ni se borran.</summary>
    private static bool IsSystemRole(ApplicationRole role) =>
        role.Name == Roles.SuperAdmin || role.Name == Roles.ProviderAdmin;

    [HasPermission(Modules.Roles, Permissions.Read)]
    public async Task<IActionResult> Index()
    {
        var roles = await BeanRoles
            .Select(r => new { r.Id, r.Name, r.DisplayName, r.Description })
            .ToListAsync();

        // La proyección a string se hace en memoria: Guid.ToString() dentro de la consulta
        // obligaría a SQL Server a convertir fila por fila.
        var model = roles
            .Select(r => new RoleViewModel
            {
                Id = r.Id.ToString(),
                Name = RoleNaming.Display(r.DisplayName, r.Name),
                Description = r.Description,
                IsSystem = r.Name == Roles.SuperAdmin || r.Name == Roles.ProviderAdmin
            })
            .OrderBy(r => r.Name)
            .ToList();

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
            // Rol de Bean: el nombre visible y el de Identity son el MISMO (sin prefijo),
            // porque estos roles pueden acabar usándose por nombre en [Authorize(Roles=...)].
            var role = new ApplicationRole(model.Name)
            {
                Description = model.Description ?? string.Empty,
                ProviderID = null,
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

    [HasPermission(Modules.Roles, Permissions.Update)]
    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        var role = await LoadBeanRoleAsync(id);
        if (role == null) return NotFound();

        if (IsSystemRole(role))
        {
            return RedirectToAction(nameof(Index));
        }

        var model = new RoleViewModel
        {
            Id = role.Id.ToString(),
            Name = RoleNaming.Display(role),
            Description = role.Description
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
            var role = await LoadBeanRoleAsync(model.Id);
            if (role == null) return NotFound();

            if (IsSystemRole(role))
            {
                return RedirectToAction(nameof(Index));
            }

            role.Name = model.Name;
            role.DisplayName = model.Name;
            role.Description = model.Description ?? string.Empty;
            role.ModifiedBy = User.Identity?.Name ?? "SYSTEM";
            role.ModifiedOn = DateTime.Now;

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
        var role = await LoadBeanRoleAsync(id);
        if (role != null && !IsSystemRole(role))
        {
            await _roleManager.DeleteAsync(role);
        }

        return RedirectToAction(nameof(Index));
    }

    [HasPermission(Modules.Roles, Permissions.Update)]
    [HttpGet]
    public async Task<IActionResult> ManagePermissions(string id)
    {
        var role = await LoadBeanRoleAsync(id);
        if (role == null) return NotFound();

        if (IsSystemRole(role))
        {
            return RedirectToAction(nameof(Index));
        }

        var model = new ManagePermissionsViewModel
        {
            RoleId = role.Id.ToString(),
            RoleName = RoleNaming.Display(role)
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
        var role = await LoadBeanRoleAsync(model.RoleId);
        if (role == null) return NotFound();

        if (IsSystemRole(role))
        {
            return RedirectToAction(nameof(Index));
        }

        var existingPermissions = await _context.Permissions
            .Where(p => p.RoleID == role.Id)
            .ToListAsync();

        // Flatten the selected permissions
        var selectedIds = model.Modules
            .SelectMany(m => m.Permissions)
            .Where(p => p.Selected)
            .Select(p => p.ParametricPermissionID)
            .Distinct()
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

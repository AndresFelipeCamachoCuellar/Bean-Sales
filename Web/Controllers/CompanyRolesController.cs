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
/// Roles PROPIOS de una empresa proveedora (<c>ProviderID == el del usuario</c>).
///
/// ─────────────────────────────────────────────────────────────────────────────────────
/// NOMBRE VISIBLE ≠ NOMBRE DE IDENTITY
/// ─────────────────────────────────────────────────────────────────────────────────────
/// Identity impone un índice único global sobre <c>NormalizedName</c>, así que dos
/// proveedores no podían llamar "Bodeguero" a sus respectivos roles: el segundo recibía
/// «Role name is already taken», mensaje que además le revelaba que otra empresa ya usaba
/// ese nombre.
///
/// Desde este cambio, lo que el usuario escribe se guarda en
/// <see cref="ApplicationRole.DisplayName"/> y el <c>Name</c> de Identity se genera con
/// <see cref="RoleNaming.ForProvider"/> (<c>p:{ProviderID:N}:{clave}</c>). La unicidad se
/// valida POR PROVEEDOR contra el nombre visible, y el mensaje de error nunca menciona a
/// otro tenant.
/// </summary>
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

        var providerId = currentUser.ProviderID.Value;

        var roles = await _roleManager.Roles
            .Where(r => r.ProviderID == providerId)
            .Select(r => new { r.Id, r.Name, r.DisplayName, r.Description })
            .ToListAsync();

        // Guid.ToString() se hace en memoria, ya materializada la consulta: dentro del
        // IQueryable obligaría a un CAST por fila y descartaría el índice.
        var model = roles
            .Select(r => new RoleViewModel
            {
                Id = r.Id.ToString(),
                Name = RoleNaming.Display(r.DisplayName, r.Name),
                Description = r.Description
            })
            .OrderBy(r => r.Name)
            .ToList();

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

        var providerId = currentUser.ProviderID.Value;

        if (ModelState.IsValid)
        {
            if (await DisplayNameTakenAsync(providerId, model.Name, excludingRoleId: null))
            {
                ModelState.AddModelError(nameof(model.Name), DuplicateNameMessage);
                return View(model);
            }

            var role = new ApplicationRole
            {
                // Nombre técnico, único por construcción dentro de este proveedor.
                Name = RoleNaming.ForProvider(providerId, model.Name),
                // Nombre visible, el único que se muestra.
                DisplayName = model.Name.Trim(),
                Description = model.Description ?? string.Empty,
                ProviderID = providerId,
                CreatedBy = User.Identity?.Name ?? "SYSTEM",
                CreatedOn = DateTime.Now,
                Status = true
            };

            var result = await _roleManager.CreateAsync(role);
            if (result.Succeeded)
            {
                return RedirectToAction(nameof(Index));
            }

            AddIdentityErrors(result);
        }
        return View(model);
    }

    [HasPermission(Modules.CompanyRoles, Permissions.Update)]
    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.ProviderID == null) return Forbid();

        var role = await LoadOwnedRoleAsync(id, currentUser.ProviderID.Value);
        if (role == null) return NotFound();

        var model = new RoleViewModel
        {
            Id = role.Id.ToString(),
            Name = RoleNaming.Display(role),
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

        var providerId = currentUser.ProviderID.Value;

        if (ModelState.IsValid)
        {
            var role = await LoadOwnedRoleAsync(model.Id, providerId);
            if (role == null) return NotFound();

            if (await DisplayNameTakenAsync(providerId, model.Name, excludingRoleId: role.Id))
            {
                ModelState.AddModelError(nameof(model.Name), DuplicateNameMessage);
                return View(model);
            }

            // ⚠️ Al renombrar cambia también el Name de Identity, y el claim de rol viaja
            // dentro de la cookie de sesión. Los usuarios que ya tuvieran sesión abierta con
            // este rol la verán refrescada por el SecurityStamp de Identity; si algún día se
            // comprobara este rol por nombre (hoy no: PermissionService resuelve por RoleId),
            // habría que forzar el re-login.
            role.Name = RoleNaming.ForProvider(providerId, model.Name);
            role.DisplayName = model.Name.Trim();
            role.Description = model.Description ?? string.Empty;
            role.ModifiedBy = User.Identity?.Name ?? "SYSTEM";
            role.ModifiedOn = DateTime.Now;

            var result = await _roleManager.UpdateAsync(role);
            if (result.Succeeded)
            {
                return RedirectToAction(nameof(Index));
            }

            AddIdentityErrors(result);
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

        var role = await LoadOwnedRoleAsync(id, currentUser.ProviderID.Value);
        if (role != null)
        {
            // Se pregunta por RoleId, no por nombre. GetUsersInRoleAsync(role.Name) resolvía
            // el rol por su nombre GLOBAL: además de ser una segunda búsqueda innecesaria,
            // era el mismo mecanismo global que rompía el multi-tenant.
            var roleInUse = await _context.UserRoles.AnyAsync(ur => ur.RoleId == role.Id);
            if (roleInUse)
            {
                TempData["CompanyRolesError"] =
                    "No se puede eliminar el rol porque todavía hay usuarios que lo tienen asignado.";
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

        var role = await LoadOwnedRoleAsync(id, currentUser.ProviderID.Value);
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

        var role = await LoadOwnedRoleAsync(model.RoleId, currentUser.ProviderID.Value);
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
    /// Mensaje ÚNICO de nombre repetido. Habla solo de "tus roles": jamás debe delatar que
    /// otra empresa usa ese mismo nombre, que es justo lo que hacía el «Role name is already
    /// taken» de Identity.
    /// </summary>
    private const string DuplicateNameMessage = "Ya tienes un rol con ese nombre. Elige otro.";

    /// <summary>
    /// Carga un rol comprobando la pertenencia al proveedor EN LA MISMA consulta, y por
    /// <c>Guid</c> (no por <c>Id.ToString()</c>, que forzaba un CAST por fila y descartaba
    /// el índice de la clave primaria).
    /// </summary>
    private async Task<ApplicationRole?> LoadOwnedRoleAsync(string? id, Guid providerId)
    {
        if (!Guid.TryParse(id, out var roleId)) return null;

        return await _roleManager.Roles
            .FirstOrDefaultAsync(r => r.Id == roleId && r.ProviderID == providerId);
    }

    /// <summary>
    /// ¿Este proveedor ya tiene un rol con ese nombre visible?
    ///
    /// La comparación se hace EN MEMORIA sobre <see cref="RoleNaming.Key"/> (minúsculas y
    /// sin tildes) y no en SQL, a propósito: SQL Server compara sin distinguir mayúsculas y
    /// SQLite sí las distingue, así que el mismo <c>==</c> daría resultados distintos en
    /// producción y en los tests. Un proveedor tiene un puñado de roles: el costo es nulo.
    /// </summary>
    private async Task<bool> DisplayNameTakenAsync(Guid providerId, string? displayName, Guid? excludingRoleId)
    {
        var candidate = RoleNaming.Key(displayName);
        if (candidate.Length == 0) return false;

        var siblings = await _roleManager.Roles
            .Where(r => r.ProviderID == providerId)
            .Select(r => new { r.Id, r.DisplayName, r.Name })
            .ToListAsync();

        return siblings.Any(r =>
            (!excludingRoleId.HasValue || r.Id != excludingRoleId.Value)
            && RoleNaming.Key(RoleNaming.Display(r.DisplayName, r.Name)) == candidate);
    }

    /// <summary>
    /// Vuelca los errores de Identity en el <c>ModelState</c>, REESCRIBIENDO el de nombre
    /// duplicado.
    ///
    /// El texto original ("Role name 'p:74390816…:bodeguero' is already taken") filtra el
    /// nombre técnico interno y, antes de este cambio, confirmaba la existencia de un rol de
    /// OTRA empresa. Con el prefijo por proveedor ya solo puede dispararse en una carrera
    /// entre dos peticiones del mismo tenant, pero el mensaje se sanea igual: la validación
    /// previa (<see cref="DisplayNameTakenAsync"/>) y ésta dicen exactamente lo mismo.
    /// </summary>
    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            if (string.Equals(error.Code, "DuplicateRoleName", StringComparison.Ordinal))
            {
                ModelState.AddModelError(nameof(RoleViewModel.Name), DuplicateNameMessage);
                continue;
            }

            ModelState.AddModelError(string.Empty, error.Description);
        }
    }

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
            RoleName = RoleNaming.Display(role)
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

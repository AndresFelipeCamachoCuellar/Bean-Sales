using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Web.Attributes;
using Web.Constants;
using Web.Models;
using Web.Models.ViewModels;
using Web.Services.Tenancy;

namespace Web.Controllers;

/// <summary>
/// Administración de usuarios desde el panel de Bean.
///
/// ─────────────────────────────────────────────────────────────────────────────────────
/// 🔒 AISLAMIENTO MULTI-TENANT
/// ─────────────────────────────────────────────────────────────────────────────────────
/// Todas las lecturas pasan por <see cref="IProviderScope"/>. Para el staff de Bean el
/// alcance es global POR DISEÑO (este es su back-office); el filtro existe porque
/// <c>Users/Read</c> y <c>Users/Update</c> son permisos delegables: el día que se le
/// concedan a alguien de una empresa proveedora, esta pantalla le mostraba —y le dejaba
/// editar y desactivar— los correos y documentos de los usuarios de TODAS las demás.
///
/// ─────────────────────────────────────────────────────────────────────────────────────
/// ROLES ASIGNABLES DESDE AQUÍ
/// ─────────────────────────────────────────────────────────────────────────────────────
/// Solo los GLOBALES de Bean (<c>ProviderID == null</c>). Los roles propios de una empresa
/// se asignan en <c>CompanyUsers</c>, dentro del panel de esa empresa. Además,
/// <see cref="RoleAssignmentError"/> impide la combinación rota que dejaba esta pantalla:
/// un usuario con rol <c>ProviderAdmin</c> pero sin <c>ProviderID</c>, que recibe 403 en
/// todo el panel del proveedor y queda inutilizable.
/// </summary>
[Authorize]
public class UsersController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly Web.Data.ApplicationDbContext _context;
    private readonly IProviderScope _scope;

    public UsersController(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        Web.Data.ApplicationDbContext context,
        IProviderScope scope)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _context = context;
        _scope = scope;
    }

    // ------------------------------------------------------------------ Helpers

    /// <summary>
    /// Roles asignables desde el panel de Bean: los GLOBALES (<c>ProviderID == null</c>).
    /// Los de una empresa proveedora se asignan desde <c>CompanyUsers</c>.
    /// </summary>
    private async Task<List<SelectListItem>> BeanRoleOptionsAsync()
    {
        var roles = await _roleManager.Roles
            .Where(r => r.ProviderID == null)
            .Select(r => new { r.Id, r.Name, r.DisplayName })
            .ToListAsync();

        // Guid.ToString() fuera del IQueryable: dentro obligaría a un CAST por fila.
        return roles
            .Select(r => new SelectListItem
            {
                Value = r.Id.ToString(),
                Text = RoleNaming.Display(r.DisplayName, r.Name)
            })
            .OrderBy(r => r.Text)
            .ToList();
    }

    private async Task<List<SelectListItem>> CountryOptionsAsync()
    {
        var countries = await _context.Countries.Select(c => new { c.CountryID, c.Name }).ToListAsync();
        return countries.Select(c => new SelectListItem { Value = c.CountryID.ToString(), Text = c.Name }).ToList();
    }

    private async Task<List<SelectListItem>> DocumentTypeOptionsAsync()
    {
        var types = await _context.DocumentTypes.Select(d => new { d.DocumentTypeID, d.Name }).ToListAsync();
        return types.Select(d => new SelectListItem { Value = d.DocumentTypeID.ToString(), Text = d.Name }).ToList();
    }

    /// <summary>
    /// ¿Es válido asignar este rol a un usuario con este <c>ProviderID</c>? Devuelve el
    /// mensaje de error, o <c>null</c> si la combinación es correcta.
    ///
    /// Cierra dos agujeros que dejaba el formulario:
    ///
    ///  1. <b>ProviderAdmin sin empresa.</b> Este formulario nunca fija el <c>ProviderID</c>,
    ///     así que asignar <c>ProviderAdmin</c> creaba un usuario que entra al sistema pero
    ///     recibe 403 en todo el panel del proveedor (cada controlador exige
    ///     <c>ProviderID != null</c>). Un usuario roto que hay que arreglar a mano en la BD.
    ///  2. <b>Rol de otra empresa.</b> Un rol con <c>ProviderID</c> solo puede ir a un
    ///     usuario de ESA empresa; cualquier otra combinación mezcla tenants.
    /// </summary>
    private static string? RoleAssignmentError(ApplicationRole role, Guid? targetProviderId)
    {
        if (role.ProviderID.HasValue)
        {
            return role.ProviderID == targetProviderId
                ? null
                : "Ese rol pertenece a una empresa proveedora y solo puede asignarse a un usuario de esa empresa, desde su propio panel.";
        }

        if (string.Equals(role.Name, Roles.ProviderAdmin, StringComparison.Ordinal) && !targetProviderId.HasValue)
        {
            return "El rol «ProviderAdmin» solo puede asignarse a un usuario vinculado a una empresa proveedora. " +
                   "Registra la empresa desde Proveedores y crea al usuario desde el panel de esa empresa.";
        }

        return null;
    }

    // ------------------------------------------------------------------ Acciones

    [HasPermission(Modules.Users, Permissions.Create)]
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var model = new CreateUserViewModel
        {
            Roles = await BeanRoleOptionsAsync(),
            Countries = await CountryOptionsAsync(),
            DocumentTypes = await DocumentTypeOptionsAsync()
        };
        return View(model);
    }

    [HasPermission(Modules.Users, Permissions.Create)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserViewModel model)
    {
        if (ModelState.IsValid)
        {
            // El rol se valida ANTES de crear al usuario: si no, una combinación inválida
            // dejaría la cuenta creada y sin rol, que es peor que no crearla.
            var selectedRole = await _roleManager.Roles.FirstOrDefaultAsync(r => r.Id == model.RoleID);
            if (selectedRole == null)
            {
                ModelState.AddModelError(nameof(model.RoleID), "Selecciona un rol válido.");
            }
            else
            {
                // Este formulario crea usuarios internos de Bean: sin ProviderID.
                var roleError = RoleAssignmentError(selectedRole, targetProviderId: null);
                if (roleError != null)
                {
                    ModelState.AddModelError(nameof(model.RoleID), roleError);
                }
            }
        }

        if (ModelState.IsValid)
        {
            var user = new ApplicationUser
            {
                UserName = model.Email, // Email as Username
                Email = model.Email,
                FirstName = model.FirstName,
                LastName = model.LastName,
                DocumentNumber = model.DocumentNumber,
                DocumentTypeID = model.DocumentTypeID,
                CountryID = model.CountryID,
                Gender = model.Gender,
                CreatedBy = User.Identity?.Name ?? "SYSTEM",
                CreatedOn = DateTime.Now,
                Status = true,
                EmailConfirmed = true,
                DateOfBirth = model.DateOfBirth 
            };

            // Correo duplicado ANTES de crear: Identity devolvería
            // "Username 'x' is already taken.", en inglés y sin indicar qué hacer.
            if (await _userManager.FindByEmailAsync(model.Email) != null)
            {
                ModelState.AddModelError(nameof(model.Email),
                    "Ya existe una cuenta con este correo. Búscala en el listado de usuarios.");
            }

            // El rol se valida ANTES de crear el usuario. El código anterior creaba el
            // usuario y, si el rol era nulo, el `if` no entraba y redirigía como si todo
            // hubiera ido bien: quedaba un usuario SIN ROL, sin permisos y sin acceso.
            var role = await _roleManager.Roles.FirstOrDefaultAsync(r => r.Id == model.RoleID);
            if (role == null)
            {
                ModelState.AddModelError(nameof(model.RoleID), "Selecciona un rol válido.");
            }

            if (ModelState.ErrorCount == 0)
            {
                var result = await _userManager.CreateAsync(user, model.Password);
                if (result.Succeeded)
                {
                    var roleResult = await _userManager.AddToRoleAsync(user, role!.Name!);
                    if (!roleResult.Succeeded)
                    {
                        // Un usuario sin rol no sirve de nada: no lo dejamos a medias.
                        await _userManager.DeleteAsync(user);
                        foreach (var error in roleResult.Errors)
                        {
                            ModelState.AddModelError(string.Empty,
                                $"No se pudo asignar el rol: {error.Description}");
                        }
                    }
                    else
                    {
                        return RedirectToAction(nameof(Index));
                    }
                }
                else
                {
                    foreach (var error in result.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }
                }
            }
        }

        // Reload lists if failed
        model.Roles = await BeanRoleOptionsAsync();
        model.Countries = await CountryOptionsAsync();
        model.DocumentTypes = await DocumentTypeOptionsAsync();

        return View(model);
    }

    [HasPermission(Modules.Users, Permissions.Update)]
    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        if (!Guid.TryParse(id, out var userId)) return NotFound();

        // Prevent editing own account
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser != null && currentUser.Id == userId)
        {
            return RedirectToAction(nameof(Index)); // Or show error message
        }

        // 🔒 Carga con comprobación de pertenencia EN LA MISMA consulta. Antes era un
        // FindByIdAsync suelto: con el ID a mano se editaba a CUALQUIER usuario, incluidos
        // los de otras empresas proveedoras.
        var scope = await _scope.CurrentAsync();
        var user = await scope.SingleOwnedSharedAsync(_userManager.Users, u => u.Id == userId);
        if (user == null) return NotFound();

        var roleId = await _context.UserRoles
            .Where(ur => ur.UserId == user.Id)
            .Select(ur => ur.RoleId)
            .FirstOrDefaultAsync();

        var model = new EditUserViewModel
        {
            Id = user.Id.ToString(),
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName,
            LastName = user.LastName,
            DocumentNumber = user.DocumentNumber ?? string.Empty,
            DocumentTypeID = user.DocumentTypeID,
            CountryID = user.CountryID,
            Gender = user.Gender,
            Status = user.Status,
            RoleID = roleId,
            Roles = await BeanRoleOptionsAsync(),
            Countries = await CountryOptionsAsync(),
            DocumentTypes = await DocumentTypeOptionsAsync()
        };

        return View(model);
    }

    [HasPermission(Modules.Users, Permissions.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditUserViewModel model)
    {
        if (!Guid.TryParse(model.Id, out var userId)) return NotFound();

        var scope = await _scope.CurrentAsync();

        // El GET redirige al listado si intentas editarte a ti mismo, pero eso es UI: sin
        // esta comprobación, un POST directo te dejaba cambiarte el rol o desactivarte.
        if (scope.CurrentUserId == userId)
        {
            return RedirectToAction(nameof(Index));
        }

        // 🔒 Igual que en el GET: el usuario objetivo se carga comprobando la pertenencia.
        var user = await scope.SingleOwnedSharedAsync(_userManager.Users, u => u.Id == userId);
        if (user == null) return NotFound();

        var newRole = await _roleManager.Roles.FirstOrDefaultAsync(r => r.Id == model.RoleID);

        if (ModelState.IsValid)
        {
            if (newRole == null)
            {
                ModelState.AddModelError(nameof(model.RoleID), "Selecciona un rol válido.");
            }
            else
            {
                // Se valida contra el ProviderID REAL del usuario, no contra el del
                // formulario: esta pantalla no lo expone y no debe poder cambiarlo.
                var roleError = RoleAssignmentError(newRole, user.ProviderID);
                if (roleError != null)
                {
                    ModelState.AddModelError(nameof(model.RoleID), roleError);
                }
            }
        }

        if (ModelState.IsValid)
        {
            user.FirstName = model.FirstName;
            user.LastName = model.LastName;
            user.DocumentNumber = model.DocumentNumber;
            user.DocumentTypeID = model.DocumentTypeID;
            user.CountryID = model.CountryID;
            user.Gender = model.Gender;
            user.Status = model.Status;

            var result = await _userManager.UpdateAsync(user);
            if (result.Succeeded)
            {
                var currentRoles = await _userManager.GetRolesAsync(user);

                if (newRole != null && !currentRoles.Contains(newRole.Name!))
                {
                    if (currentRoles.Any())
                    {
                        await _userManager.RemoveFromRolesAsync(user, currentRoles);
                    }
                    await _userManager.AddToRoleAsync(user, newRole.Name!);
                }

                return RedirectToAction(nameof(Index));
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }

        model.Roles = await BeanRoleOptionsAsync();
        model.Countries = await CountryOptionsAsync();
        model.DocumentTypes = await DocumentTypeOptionsAsync();

        return View(model);
    }

    [HasPermission(Modules.Users, Permissions.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStatus(string id)
    {
        if (!Guid.TryParse(id, out var userId)) return NotFound();

        // 🔒 Antes bastaba con el ID: se podía desactivar la cuenta de un usuario de otra
        // empresa proveedora y dejarla sin acceso a su propio panel.
        var scope = await _scope.CurrentAsync();
        var user = await scope.SingleOwnedSharedAsync(_userManager.Users, u => u.Id == userId);
        if (user == null) return NotFound();

        if (scope.CurrentUserId == user.Id)
        {
            return BadRequest("No puedes desactivar tu propia cuenta.");
        }

        user.Status = !user.Status;
        await _userManager.UpdateAsync(user);

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Permisos EFECTIVOS de un usuario y de qué rol le viene cada uno (HU-5.5).
    ///
    /// Solo lectura y deliberadamente sin acciones: es una herramienta de diagnóstico. La
    /// pregunta que responde ("¿por qué este proveedor ve la pantalla de Precios?") hoy solo
    /// se puede contestar consultando la BD a mano, y eso es exactamente el terreno donde se
    /// escapan los errores de permisos.
    ///
    /// Se protege con <c>Users/Read</c>, el mismo permiso que ya deja ver el listado de
    /// usuarios: no expone nada que ese permiso no exponga ya, solo lo ordena.
    /// </summary>
    [HasPermission(Modules.Users, Permissions.Read)]
    [HttpGet]
    public async Task<IActionResult> EffectivePermissions(Guid id)
    {
        if (id == Guid.Empty) return NotFound();

        // 🔒 Aislamiento multi-tenant desde el minuto cero (E5.2). Hoy Users/Read solo lo
        // tiene el SuperAdmin, así que el filtro no cambia nada; pero si mañana ese permiso
        // se delegara a un proveedor, esta pantalla le mostraría los roles y permisos de
        // usuarios de OTRAS empresas. Para el staff de Bean, ApplyToShared no filtra.
        var scope = await _scope.CurrentAsync();

        var user = await scope.SingleOwnedSharedAsync(
            _userManager.Users.Include(u => u.Provider),
            u => u.Id == id);

        if (user == null) return NotFound();

        var model = new EffectivePermissionsViewModel
        {
            UserId = user.Id.ToString(),
            UserName = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            FullName = $"{user.FirstName} {user.LastName}".Trim(),
            ProviderName = user.Provider?.Name
        };

        // 1. Roles del usuario. Se consultan por Id (no por nombre) para no depender de la
        //    normalización de Identity.
        var roleIds = await _context.UserRoles
            .Where(ur => ur.UserId == user.Id)
            .Select(ur => ur.RoleId)
            .ToListAsync();

        if (roleIds.Count == 0)
        {
            return View(model);
        }

        var roleRows = await _context.Roles
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Name, r.DisplayName, r.Description, r.ProviderID })
            .ToListAsync();

        // Se muestra el nombre VISIBLE: para un rol de proveedor, el Name de Identity es el
        // identificador técnico "p:{guid}:{clave}" y no le dice nada a quien audita.
        var roles = roleRows
            .Select(r => new EffectiveRoleViewModel
            {
                RoleId = r.Id,
                Name = RoleNaming.Display(r.DisplayName, r.Name),
                Description = r.Description,
                ProviderID = r.ProviderID
            })
            .ToList();

        // 2. Concesiones: una fila por (rol, permiso). Es el JOIN que hoy hay que hacer a
        //    mano en SQL Server Management Studio.
        var grants = await _context.Permissions
            .Where(p => roleIds.Contains(p.RoleID) && p.Status)
            .Select(p => new
            {
                p.RoleID,
                ModuleCode = p.ParametricPermission!.Module!.Code,
                ModuleName = p.ParametricPermission!.Module!.Name,
                PermissionCode = p.ParametricPermission!.Code,
                PermissionName = p.ParametricPermission!.Name,
                Description = p.ParametricPermission!.Description
            })
            .ToListAsync();

        // 3. Agrupación en memoria: son decenas de filas, no millones, y agrupar en SQL
        //    obligaría a una segunda consulta para resolver los nombres de rol.
        var roleNameById = roles.ToDictionary(r => r.RoleId, r => r.Name);

        foreach (var role in roles)
        {
            role.PermissionCount = grants.Count(g => g.RoleID == role.RoleId);
        }

        model.Roles = roles
            .OrderByDescending(r => r.IsGlobal)
            .ThenBy(r => r.Name)
            .ToList();

        model.Modules = grants
            .GroupBy(g => new { g.ModuleCode, g.ModuleName })
            .Select(moduleGroup => new EffectiveModuleViewModel
            {
                ModuleCode = moduleGroup.Key.ModuleCode,
                ModuleName = moduleGroup.Key.ModuleName,
                Permissions = moduleGroup
                    .GroupBy(g => new { g.PermissionCode, g.PermissionName, g.Description })
                    .Select(permissionGroup => new EffectivePermissionViewModel
                    {
                        PermissionCode = permissionGroup.Key.PermissionCode,
                        PermissionName = permissionGroup.Key.PermissionName,
                        Description = permissionGroup.Key.Description,
                        // Varios roles pueden conceder el MISMO permiso: se listan todos,
                        // porque revocarlo exige quitarlo de cada uno.
                        GrantedBy = permissionGroup
                            .Select(g => roleNameById.TryGetValue(g.RoleID, out var n) ? n : "—")
                            .Distinct()
                            .OrderBy(n => n)
                            .ToList()
                    })
                    .OrderBy(p => p.PermissionCode)
                    .ToList()
            })
            .OrderBy(m => m.ModuleName)
            .ToList();

        return View(model);
    }

    [HasPermission(Modules.Users, Permissions.Read)]
    public async Task<IActionResult> Index()
    {
        // 🔒 Antes listaba a TODOS los usuarios del sistema —correo y número de documento
        // incluidos—, también los de las empresas proveedoras. Para el staff de Bean el
        // alcance sigue siendo global (es su back-office); para un usuario de proveedor,
        // ApplyToShared lo recorta a su propia empresa.
        var scope = await _scope.CurrentAsync();

        var users = await scope.ApplyToShared(_userManager.Users)
            .OrderBy(u => u.FirstName)
            .ThenBy(u => u.LastName)
            .ToListAsync();

        if (users.Count == 0)
        {
            return View(new List<UserViewModel>());
        }

        // Los roles se resuelven en UNA consulta en vez de un GetRolesAsync por usuario
        // (N+1), y se muestran por su nombre VISIBLE: el Name de Identity de un rol de
        // proveedor es el identificador técnico "p:{guid}:{clave}", que no significa nada
        // para quien lee la tabla.
        var userIds = users.Select(u => u.Id).ToList();

        var roleRows = await (
            from userRole in _context.UserRoles
            join role in _context.Roles on userRole.RoleId equals role.Id
            where userIds.Contains(userRole.UserId)
            select new { userRole.UserId, role.Name, role.DisplayName })
            .ToListAsync();

        var rolesByUser = roleRows
            .GroupBy(r => r.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => RoleNaming.Display(r.DisplayName, r.Name)).ToList());

        var userViewModels = users.Select(user => new UserViewModel
        {
            Id = user.Id.ToString(),
            UserName = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            FullName = $"{user.FirstName} {user.LastName}",
            DocumentNumber = user.DocumentNumber ?? string.Empty,
            Status = user.Status,
            Roles = rolesByUser.TryGetValue(user.Id, out var roles) ? roles : new List<string>()
        }).ToList();

        return View(userViewModels);
    }
}

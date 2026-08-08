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
/// Usuarios de UNA empresa proveedora, gestionados por ella misma.
///
/// El aislamiento ya estaba (todo filtra por <c>ProviderID</c>), pero se hacía comparando
/// <c>u.Id.ToString() == id</c>: eso obliga a SQL Server a convertir el GUID a texto FILA POR
/// FILA y descarta el índice de la clave primaria. Ahora se compara por <c>Guid</c>, igual
/// que ya se corrigió en <c>ProductsController</c>.
/// </summary>
[Authorize]
public class CompanyUsersController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly Web.Data.ApplicationDbContext _context;

    public CompanyUsersController(UserManager<ApplicationUser> userManager, RoleManager<ApplicationRole> roleManager, Web.Data.ApplicationDbContext context)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _context = context;
    }

    /// <summary>
    /// Roles asignables dentro de la empresa: los suyos propios más el global
    /// <c>ProviderAdmin</c> (para que pueda nombrar un administrador de respaldo).
    /// Se muestran por su nombre VISIBLE: el <c>Name</c> de Identity de un rol de proveedor
    /// es el identificador técnico <c>p:{guid}:{clave}</c>.
    /// </summary>
    private async Task<List<SelectListItem>> AssignableRoleOptionsAsync(Guid providerId)
    {
        var roles = await _roleManager.Roles
            .Where(r => r.ProviderID == providerId || r.Name == Roles.ProviderAdmin)
            .Select(r => new { r.Id, r.Name, r.DisplayName })
            .ToListAsync();

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
        // El Guid.ToString() se hace tras materializar: dentro de la consulta obliga a un
        // CAST por fila.
        var countries = await _context.Countries.Select(c => new { c.CountryID, c.Name }).ToListAsync();
        return countries.Select(c => new SelectListItem { Value = c.CountryID.ToString(), Text = c.Name }).ToList();
    }

    private async Task<List<SelectListItem>> DocumentTypeOptionsAsync()
    {
        var types = await _context.DocumentTypes.Select(d => new { d.DocumentTypeID, d.Name }).ToListAsync();
        return types.Select(d => new SelectListItem { Value = d.DocumentTypeID.ToString(), Text = d.Name }).ToList();
    }

    /// <summary>
    /// Carga un usuario de la empresa por ID comprobando la pertenencia en la MISMA consulta.
    /// </summary>
    private async Task<ApplicationUser?> LoadCompanyUserAsync(string? id, Guid providerId)
    {
        if (!Guid.TryParse(id, out var userId)) return null;

        return await _userManager.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.ProviderID == providerId);
    }

    /// <summary>¿Este rol se puede asignar dentro de esta empresa?</summary>
    private static bool IsAssignable(ApplicationRole role, Guid providerId) =>
        role.ProviderID == providerId || role.Name == Roles.ProviderAdmin;

    [HasPermission(Modules.CompanyUsers, Permissions.Read)]
    public async Task<IActionResult> Index()
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.ProviderID == null)
        {
             return RedirectToAction("AccessDenied", "Account");
        }

        var providerId = currentUser.ProviderID.Value;

        var users = await _userManager.Users
            .Where(u => u.ProviderID == providerId)
            .OrderBy(u => u.FirstName)
            .ThenBy(u => u.LastName)
            .ToListAsync();

        if (users.Count == 0)
        {
            return View(new List<UserViewModel>());
        }

        // Una consulta para todos los roles en vez de un GetRolesAsync por usuario (N+1).
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

    [HasPermission(Modules.CompanyUsers, Permissions.Create)]
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.ProviderID == null) return RedirectToAction("AccessDenied", "Account");

        var model = new CreateUserViewModel
        {
            Roles = await AssignableRoleOptionsAsync(currentUser.ProviderID.Value),
            Countries = await CountryOptionsAsync(),
            DocumentTypes = await DocumentTypeOptionsAsync()
        };
        return View(model);
    }

    [HasPermission(Modules.CompanyUsers, Permissions.Create)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserViewModel model)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.ProviderID == null) return RedirectToAction("AccessDenied", "Account");

        var providerId = currentUser.ProviderID.Value;

        if (ModelState.IsValid)
        {
            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                FirstName = model.FirstName,
                LastName = model.LastName,
                DocumentNumber = model.DocumentNumber,
                DocumentTypeID = model.DocumentTypeID,
                CountryID = model.CountryID,
                Gender = model.Gender,
                CreatedBy = User.Identity?.Name ?? "PROVIDER_ADMIN",
                CreatedOn = DateTime.Now,
                Status = true,
                EmailConfirmed = true,
                DateOfBirth = model.DateOfBirth,
                ProviderID = providerId // Automatically assign current provider
            };

            // 1. El correo, ANTES de crear nada. Identity devolvería
            //    "Username 'x' is already taken." — en inglés y sin decir qué hacer.
            var yaExiste = await _userManager.FindByEmailAsync(model.Email) != null;
            if (yaExiste)
            {
                ModelState.AddModelError(nameof(model.Email),
                    "Ya existe una cuenta con este correo. Si acabas de crearla, búscala en el listado de usuarios; " +
                    "si pertenece a otra empresa, usa un correo distinto.");
            }

            // 2. El ROL se valida ANTES de crear el usuario.
            //    ANTES: se creaba el usuario y, si el rol era nulo o no asignable, el
            //    `if` simplemente no entraba y se redirigía como si todo hubiera salido
            //    bien. Resultado: un usuario SIN NINGÚN ROL, es decir sin permisos y sin
            //    acceso al panel, y nadie se enteraba. Fallar antes evita el huérfano.
            var role = await _roleManager.Roles.FirstOrDefaultAsync(r => r.Id == model.RoleID);
            if (role == null || !IsAssignable(role, providerId))
            {
                ModelState.AddModelError(nameof(model.RoleID),
                    "Selecciona un rol válido de tu empresa.");
            }

            if (ModelState.ErrorCount == 0)
            {
                var result = await _userManager.CreateAsync(user, model.Password);
                if (result.Succeeded)
                {
                    var roleResult = await _userManager.AddToRoleAsync(user, role!.Name!);
                    if (!roleResult.Succeeded)
                    {
                        // Un usuario sin rol no puede hacer nada: no lo dejamos a medias.
                        await _userManager.DeleteAsync(user);
                        foreach (var error in roleResult.Errors)
                        {
                            ModelState.AddModelError(string.Empty,
                                $"No se pudo asignar el rol: {error.Description}");
                        }
                    }
                    else
                    {
                        TempData["CompanyUserCreated"] =
                            $"Usuario {user.Email} creado y asignado al rol {role.DisplayName ?? role.Name}.";
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

        // Reload lists
        model.Roles = await AssignableRoleOptionsAsync(providerId);
        model.Countries = await CountryOptionsAsync();
        model.DocumentTypes = await DocumentTypeOptionsAsync();

        return View(model);
    }

    [HasPermission(Modules.CompanyUsers, Permissions.Update)]
    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.ProviderID == null) return RedirectToAction("AccessDenied", "Account");

        var providerId = currentUser.ProviderID.Value;

        // Important: Ensure the target user belongs to the SAME provider.
        var user = await LoadCompanyUserAsync(id, providerId);

        // Prevent self-editing
        if (user != null && user.Id == currentUser.Id)
        {
             return RedirectToAction(nameof(Index));
        }

        if (user == null) return NotFound();

        // El rol se resuelve por RoleId, no por nombre: FindByNameAsync buscaba en el índice
        // GLOBAL de nombres, el mismo mecanismo que rompía el multi-tenant.
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
            Roles = await AssignableRoleOptionsAsync(providerId),
            Countries = await CountryOptionsAsync(),
            DocumentTypes = await DocumentTypeOptionsAsync()
        };

        return View(model);
    }

    [HasPermission(Modules.CompanyUsers, Permissions.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditUserViewModel model)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.ProviderID == null) return RedirectToAction("AccessDenied", "Account");

        var providerId = currentUser.ProviderID.Value;

        if (ModelState.IsValid)
        {
            // Security check: Target user must belong to same provider
            var user = await LoadCompanyUserAsync(model.Id, providerId);

            // Prevent self-editing
            if (user != null && user.Id == currentUser.Id)
            {
                 return BadRequest("Cannot edit your own account from this view.");
            }
            if (user == null) return NotFound();

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
                var curRoles = await _userManager.GetRolesAsync(user);
                var newRole = await _roleManager.Roles.FirstOrDefaultAsync(r => r.Id == model.RoleID);

                if (newRole != null && IsAssignable(newRole, providerId) && !curRoles.Contains(newRole.Name!))
                {
                    if (curRoles.Any())
                    {
                        await _userManager.RemoveFromRolesAsync(user, curRoles);
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

        model.Roles = await AssignableRoleOptionsAsync(providerId);
        model.Countries = await CountryOptionsAsync();
        model.DocumentTypes = await DocumentTypeOptionsAsync();

        return View(model);
    }
    
    [HasPermission(Modules.CompanyUsers, Permissions.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStatus(string id)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.ProviderID == null) return NotFound();

        var user = await LoadCompanyUserAsync(id, currentUser.ProviderID.Value);
        if (user == null) return NotFound();

        if (currentUser.Id == user.Id)
        {
             return BadRequest("No puedes desactivar tu propia cuenta.");
        }

        user.Status = !user.Status;
        await _userManager.UpdateAsync(user);
        
        return RedirectToAction(nameof(Index));
    }
}

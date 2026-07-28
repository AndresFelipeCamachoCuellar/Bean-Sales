using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Web.Attributes;
using Web.Constants;
using Web.Models;
using Web.Models.ViewModels;

namespace Web.Controllers;

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

    [HasPermission(Modules.CompanyUsers, Permissions.Read)]
    public async Task<IActionResult> Index()
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.ProviderID == null)
        {
             return RedirectToAction("AccessDenied", "Account");
        }

        var users = await _userManager.Users
            .Where(u => u.ProviderID == currentUser.ProviderID)
            .ToListAsync();

        var userViewModels = new List<UserViewModel>();

        foreach (var user in users)
        {
            var thisViewModel = new UserViewModel
            {
                Id = user.Id.ToString(),
                UserName = user.UserName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                FullName = $"{user.FirstName} {user.LastName}",
                DocumentNumber = user.DocumentNumber ?? string.Empty,
                Status = user.Status,
                Roles = await _userManager.GetRolesAsync(user)
            };
            userViewModels.Add(thisViewModel);
        }

        return View(userViewModels);
    }

    [HasPermission(Modules.CompanyUsers, Permissions.Create)]
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.ProviderID == null) return RedirectToAction("AccessDenied", "Account");
        
        // Only allow assigning roles that are NOT System Restricted (SuperAdmin)
        // Ideally, we should filter roles that belong to this Provider OR are Basic.
        // For now, let's filter out SuperAdmin and ProviderAdmin (self-assignment protection?). 
        // Actually, a ProviderAdmin might want to create another ProviderAdmin for backup.
        // Let's just exclude SuperAdmin.

        var rolesQuery = _roleManager.Roles.Where(r => r.ProviderID == currentUser.ProviderID || r.Name == Roles.ProviderAdmin);
        
        // TODO: Future enhancement: Filter roles by ProviderID == null OR ProviderID == currentUser.ProviderID

        var model = new CreateUserViewModel
        {
            Roles = await rolesQuery.Select(r => new SelectListItem { Value = r.Id.ToString(), Text = r.Name }).ToListAsync(),
            Countries = await _context.Countries.Select(c => new SelectListItem { Value = c.CountryID.ToString(), Text = c.Name }).ToListAsync(),
            DocumentTypes = await _context.DocumentTypes.Select(d => new SelectListItem { Value = d.DocumentTypeID.ToString(), Text = d.Name }).ToListAsync()
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
                ProviderID = currentUser.ProviderID // Automatically assign current provider
            };

            var result = await _userManager.CreateAsync(user, model.Password);
            if (result.Succeeded)
            {
                var role = await _roleManager.FindByIdAsync(model.RoleID.ToString());
                if (role != null && (role.ProviderID == currentUser.ProviderID || role.Name == Roles.ProviderAdmin))
                {
                    await _userManager.AddToRoleAsync(user, role.Name!);
                }
                return RedirectToAction(nameof(Index));
            }
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }

        // Reload lists
        var rolesQuery = _roleManager.Roles.Where(r => r.ProviderID == currentUser.ProviderID || r.Name == Roles.ProviderAdmin);
        model.Roles = await rolesQuery.Select(r => new SelectListItem { Value = r.Id.ToString(), Text = r.Name }).ToListAsync();
        model.Countries = await _context.Countries.Select(c => new SelectListItem { Value = c.CountryID.ToString(), Text = c.Name }).ToListAsync();
        model.DocumentTypes = await _context.DocumentTypes.Select(d => new SelectListItem { Value = d.DocumentTypeID.ToString(), Text = d.Name }).ToListAsync();
        
        return View(model);
    }

    [HasPermission(Modules.CompanyUsers, Permissions.Update)]
    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        if (string.IsNullOrEmpty(id)) return NotFound();
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.ProviderID == null) return RedirectToAction("AccessDenied", "Account");

        // Prevent editing own account from this screen? Maybe allowed.
        // Important: Ensure the target user belongs to the SAME provider.
        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Id.ToString() == id && u.ProviderID == currentUser.ProviderID);

        // Prevent self-editing
        if (user != null && user.Id == currentUser.Id)
        {
             return RedirectToAction(nameof(Index));
        }

        if (user == null) return NotFound();

        var userRoles = await _userManager.GetRolesAsync(user);
        var roleId = Guid.Empty;
        if (userRoles.Any())
        {
            var role = await _roleManager.FindByNameAsync(userRoles.First());
            if (role != null) roleId = role.Id;
        }

        var rolesQuery = _roleManager.Roles.Where(r => r.ProviderID == currentUser.ProviderID || r.Name == Roles.ProviderAdmin);

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
            Roles = await rolesQuery.Select(r => new SelectListItem { Value = r.Id.ToString(), Text = r.Name }).ToListAsync(),
            Countries = await _context.Countries.Select(c => new SelectListItem { Value = c.CountryID.ToString(), Text = c.Name }).ToListAsync(),
            DocumentTypes = await _context.DocumentTypes.Select(d => new SelectListItem { Value = d.DocumentTypeID.ToString(), Text = d.Name }).ToListAsync()
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

        if (ModelState.IsValid)
        {
            // Security check: Target user must belong to same provider
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Id.ToString() == model.Id && u.ProviderID == currentUser.ProviderID);
            
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
                var newRole = await _roleManager.FindByIdAsync(model.RoleID.ToString());
                
                if (newRole != null && (newRole.ProviderID == currentUser.ProviderID || newRole.Name == Roles.ProviderAdmin) && !curRoles.Contains(newRole.Name!))
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

        var rolesQuery = _roleManager.Roles.Where(r => r.ProviderID == currentUser.ProviderID || r.Name == Roles.ProviderAdmin);
        model.Roles = await rolesQuery.Select(r => new SelectListItem { Value = r.Id.ToString(), Text = r.Name }).ToListAsync();
        model.Countries = await _context.Countries.Select(c => new SelectListItem { Value = c.CountryID.ToString(), Text = c.Name }).ToListAsync();
        model.DocumentTypes = await _context.DocumentTypes.Select(d => new SelectListItem { Value = d.DocumentTypeID.ToString(), Text = d.Name }).ToListAsync();
        
        return View(model);
    }
    
    [HasPermission(Modules.CompanyUsers, Permissions.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStatus(string id)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser?.ProviderID == null) return NotFound();

        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Id.ToString() == id && u.ProviderID == currentUser.ProviderID);
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

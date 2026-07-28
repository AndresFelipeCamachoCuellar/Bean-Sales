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
public class UsersController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly Web.Data.ApplicationDbContext _context;

    public UsersController(UserManager<ApplicationUser> userManager, RoleManager<ApplicationRole> roleManager, Web.Data.ApplicationDbContext context)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _context = context;
    }

    [HasPermission(Modules.Users, Permissions.Create)]
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var model = new CreateUserViewModel
        {
            Roles = await _roleManager.Roles.Select(r => new SelectListItem { Value = r.Id.ToString(), Text = r.Name }).ToListAsync(),
            Countries = await _context.Countries.Select(c => new SelectListItem { Value = c.CountryID.ToString(), Text = c.Name }).ToListAsync(),
            DocumentTypes = await _context.DocumentTypes.Select(d => new SelectListItem { Value = d.DocumentTypeID.ToString(), Text = d.Name }).ToListAsync()
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

            var result = await _userManager.CreateAsync(user, model.Password);
            if (result.Succeeded)
            {
                var role = await _roleManager.FindByIdAsync(model.RoleID.ToString());
                if (role != null)
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

        // Reload lists if failed
        model.Roles = await _roleManager.Roles.Select(r => new SelectListItem { Value = r.Id.ToString(), Text = r.Name }).ToListAsync();
        model.Countries = await _context.Countries.Select(c => new SelectListItem { Value = c.CountryID.ToString(), Text = c.Name }).ToListAsync();
        model.DocumentTypes = await _context.DocumentTypes.Select(d => new SelectListItem { Value = d.DocumentTypeID.ToString(), Text = d.Name }).ToListAsync();
        
        return View(model);
    }

    [HasPermission(Modules.Users, Permissions.Update)]
    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        if (string.IsNullOrEmpty(id)) return NotFound();

        // Prevent editing own account
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser != null && currentUser.Id.ToString() == id)
        {
            return RedirectToAction(nameof(Index)); // Or show error message
        }

        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        var userRoles = await _userManager.GetRolesAsync(user);
        var roleId = Guid.Empty;
        if (userRoles.Any())
        {
            var role = await _roleManager.FindByNameAsync(userRoles.First());
            if (role != null) roleId = role.Id;
        }

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
            Roles = await _roleManager.Roles.Select(r => new SelectListItem { Value = r.Id.ToString(), Text = r.Name }).ToListAsync(),
            Countries = await _context.Countries.Select(c => new SelectListItem { Value = c.CountryID.ToString(), Text = c.Name }).ToListAsync(),
            DocumentTypes = await _context.DocumentTypes.Select(d => new SelectListItem { Value = d.DocumentTypeID.ToString(), Text = d.Name }).ToListAsync()
        };

        return View(model);
    }

    [HasPermission(Modules.Users, Permissions.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditUserViewModel model)
    {
        if (ModelState.IsValid)
        {
            var user = await _userManager.FindByIdAsync(model.Id);
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
                var currentRoles = await _userManager.GetRolesAsync(user);
                var newRole = await _roleManager.FindByIdAsync(model.RoleID.ToString());
                
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

        model.Roles = await _roleManager.Roles.Select(r => new SelectListItem { Value = r.Id.ToString(), Text = r.Name }).ToListAsync();
        model.Countries = await _context.Countries.Select(c => new SelectListItem { Value = c.CountryID.ToString(), Text = c.Name }).ToListAsync();
        model.DocumentTypes = await _context.DocumentTypes.Select(d => new SelectListItem { Value = d.DocumentTypeID.ToString(), Text = d.Name }).ToListAsync();
        
        return View(model);
    }

    [HasPermission(Modules.Users, Permissions.Update)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStatus(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        if (User.Identity?.Name == user.UserName)
        {
            return BadRequest("You cannot deactivate your own account.");
        }

        user.Status = !user.Status;
        await _userManager.UpdateAsync(user);
        
        return RedirectToAction(nameof(Index));
    }

    [HasPermission(Modules.Users, Permissions.Read)]
    public async Task<IActionResult> Index()
    {
        var users = await _userManager.Users.ToListAsync();
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
}

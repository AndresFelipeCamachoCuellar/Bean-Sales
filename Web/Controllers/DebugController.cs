using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Web.Constants;
using Web.Data;
using Web.Models;
using Web.Services;

namespace Web.Controllers;

public class DebugController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly IPermissionService _permissionService;

    public DebugController(UserManager<ApplicationUser> userManager, ApplicationDbContext context, IPermissionService permissionService)
    {
        _userManager = userManager;
        _context = context;
        _permissionService = permissionService;
    }

    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Content("User not logged in");

        var roles = await _userManager.GetRolesAsync(user);
        
        var moduleExists = await _context.ParametricModules.AnyAsync(m => m.Code == Modules.Users);
        var permExists = await _context.ParametricPermissions.AnyAsync(p =>
            p.Code == Permissions.Read && p.Module != null && p.Module.Code == Modules.Users);
        
        // Check raw permissions in DB
        var superAdminRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == Roles.SuperAdmin);
        var permissionsCount = 0;
        if (superAdminRole != null)
        {
            permissionsCount = await _context.Permissions.CountAsync(p => p.RoleID == superAdminRole.Id);
        }

        var hasPermission = await _permissionService.HasPermissionAsync(user, Modules.Users, Permissions.Read);

        return Json(new
        {
            UserName = user.UserName,
            Roles = roles,
            Modules_Users_Exists = moduleExists,
            Permission_Read_Exists = permExists,
            SuperAdmin_Role_Found = superAdminRole != null,
            Permissions_Assigned_To_SuperAdmin_Count = permissionsCount,
            HasPermission_Service_Result = hasPermission
        });
    }
}

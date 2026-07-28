using Microsoft.EntityFrameworkCore;
using Web.Data;
using Web.Models;

namespace Web.Services;

public interface IPermissionService
{
    Task<bool> HasPermissionAsync(ApplicationUser user, string moduleCode, string permissionCode);
}

public class PermissionService : IPermissionService
{
    private readonly ApplicationDbContext _context;

    public PermissionService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<bool> HasPermissionAsync(ApplicationUser user, string moduleCode, string permissionCode)
    {
        // 1. Get User Roles
        var roleIds = await _context.UserRoles
            .Where(ur => ur.UserId == user.Id)
            .Select(ur => ur.RoleId)
            .ToListAsync();

        if (!roleIds.Any()) return false;

        // 2. Check if any Role has the Permission for the Module
        var hasPermission = await _context.Permissions
            .Include(p => p.ParametricPermission)
                .ThenInclude(pp => pp.Module)
            .AnyAsync(p => 
                roleIds.Contains(p.RoleID) &&
                p.ParametricPermission != null &&
                p.ParametricPermission.Module != null &&
                p.ParametricPermission.Module.Code == moduleCode &&
                p.ParametricPermission.Code == permissionCode &&
                p.Status == true
            );

        return hasPermission;
    }
}

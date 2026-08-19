using Microsoft.EntityFrameworkCore;
using Web.Data;
using Web.Models;

namespace Web.Services;

public interface IPermissionService
{
    Task<bool> HasPermissionAsync(ApplicationUser? user, string moduleCode, string permissionCode);
    Task<IReadOnlySet<string>> GetAllowedModulesAsync(
        ApplicationUser user,
        string permissionCode,
        IEnumerable<string> moduleCodes);
}

public class PermissionService : IPermissionService
{
    private readonly ApplicationDbContext _context;

    public PermissionService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<bool> HasPermissionAsync(ApplicationUser? user, string moduleCode, string permissionCode)
    {
        if (user is null) return false;

        var allowedModules = await GetAllowedModulesAsync(user, permissionCode, [moduleCode]);
        return allowedModules.Contains(moduleCode);
    }

    public async Task<IReadOnlySet<string>> GetAllowedModulesAsync(
        ApplicationUser user,
        string permissionCode,
        IEnumerable<string> moduleCodes)
    {
        var requestedModules = moduleCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (requestedModules.Length == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var allowedModules = await (
            from permission in _context.Permissions.AsNoTracking()
            join userRole in _context.UserRoles on permission.RoleID equals userRole.RoleId
            join role in _context.Roles on userRole.RoleId equals role.Id
            join parametricPermission in _context.ParametricPermissions
                on permission.ParametricPermissionID equals parametricPermission.ParametricPermissionID
            join module in _context.ParametricModules
                on parametricPermission.ModuleID equals module.ModuleID
            where userRole.UserId == user.Id
                && userRole.Status
                && role.Status
                && permission.Status
                && parametricPermission.Status
                && module.Status
                && parametricPermission.Code == permissionCode
                && requestedModules.Contains(module.Code)
            select module.Code)
            .Distinct()
            .ToListAsync();

        return allowedModules.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}

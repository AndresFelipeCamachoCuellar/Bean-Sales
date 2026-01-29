using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Web.Models;
using Web.Services;

namespace Web.Filters;

public class PermissionFilter : IAsyncAuthorizationFilter
{
    private readonly IPermissionService _permissionService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly string _module;
    private readonly string _permission;

    public PermissionFilter(IPermissionService permissionService, UserManager<ApplicationUser> userManager, string module, string permission)
    {
        _permissionService = permissionService;
        _userManager = userManager;
        _module = module;
        _permission = permission;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (!user.Identity?.IsAuthenticated ?? true)
        {
            context.Result = new ChallengeResult();
            return;
        }

        var appUser = await _userManager.GetUserAsync(user);
        if (appUser == null)
        {
            context.Result = new ForbidResult();
            return;
        }

        var hasPermission = await _permissionService.HasPermissionAsync(appUser, _module, _permission);
        if (!hasPermission)
        {
            context.Result = new ForbidResult();
        }
    }
}

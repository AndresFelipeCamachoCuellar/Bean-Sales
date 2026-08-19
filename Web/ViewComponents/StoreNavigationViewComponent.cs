using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Web.Constants;
using Web.Data;
using Web.Models;
using Web.Models.Enums;
using Web.Models.ViewModels;
using Web.Services;

namespace Web.ViewComponents;

public sealed class StoreNavigationViewComponent : ViewComponent
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPermissionService _permissionService;
    private readonly ApplicationDbContext _context;

    public StoreNavigationViewComponent(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IPermissionService permissionService,
        ApplicationDbContext context)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _permissionService = permissionService;
        _context = context;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var isSignedIn = _signInManager.IsSignedIn(HttpContext.User);
        var user = isSignedIn ? await _userManager.GetUserAsync(HttpContext.User) : null;
        var isSuperAdmin = isSignedIn && HttpContext.User.IsInRole(Roles.SuperAdmin);
        var isProvider = user?.ProviderID.HasValue == true;

        var canReadUsers = false;
        var canReadRoles = false;
        var canReadApprovals = false;
        var canReadProducts = false;
        var canReadCompanyProfile = false;

        if (user != null)
        {
            string[] requestedModules = isSuperAdmin
                ? new[] { Modules.Users, Modules.Roles, Modules.ProductApprovals }
                : isProvider
                    ? new[] { Modules.Products, Modules.CompanyProfile }
                    : [];
            var allowedModules = await _permissionService.GetAllowedModulesAsync(
                user,
                Permissions.Read,
                requestedModules);

            canReadUsers = allowedModules.Contains(Modules.Users);
            canReadRoles = allowedModules.Contains(Modules.Roles);
            canReadApprovals = allowedModules.Contains(Modules.ProductApprovals);
            canReadProducts = allowedModules.Contains(Modules.Products);
            canReadCompanyProfile = allowedModules.Contains(Modules.CompanyProfile);
        }

        var pendingApprovals = canReadApprovals
            ? await _context.Products.CountAsync(product =>
                product.ProductStatus == ProductStatus.PendingApproval && product.Status)
            : 0;

        var cartCount = 0;
        if (user != null)
        {
            cartCount = await _context.ShoppingCartItems
                .Where(item => item.UserID == user.Id)
                .SumAsync(item => (int?)item.Quantity) ?? 0;
        }
        else
        {
            var cartJson = HttpContext.Session.GetString("ShoppingCart");
            if (!string.IsNullOrWhiteSpace(cartJson))
            {
                var cartItems = System.Text.Json.JsonSerializer.Deserialize<List<ShoppingCartItem>>(cartJson);
                cartCount = cartItems?.Sum(item => item.Quantity) ?? 0;
            }
        }

        var displayName = !string.IsNullOrWhiteSpace(user?.FirstName)
            ? user.FirstName
            : (HttpContext.User.Identity?.Name ?? "Mi cuenta");
        var trimmedName = displayName.Trim();
        var initial = trimmedName.Length > 0 ? trimmedName[..1].ToUpperInvariant() : "U";

        var panelController = isSuperAdmin
            ? "Dashboard"
            : canReadProducts
                ? "Products"
                : "CompanyProfile";

        var controller = ViewContext.RouteData.Values["controller"]?.ToString() ?? string.Empty;
        var action = ViewContext.RouteData.Values["action"]?.ToString() ?? string.Empty;
        var isHome = string.Equals(controller, "Home", StringComparison.OrdinalIgnoreCase);

        return View(new StoreNavigationViewModel
        {
            IsSignedIn = isSignedIn,
            IsSuperAdmin = isSuperAdmin,
            IsProvider = isProvider,
            CanReadUsers = canReadUsers,
            CanReadRoles = canReadRoles,
            CanReadApprovals = canReadApprovals,
            CanReadProducts = canReadProducts,
            CanReadCompanyProfile = canReadCompanyProfile,
            PendingApprovals = pendingApprovals,
            CartCount = cartCount,
            DisplayName = displayName,
            Initial = initial,
            RoleLabel = isSuperAdmin ? "Administrador" : isProvider ? "Proveedor" : "Cliente",
            PanelController = panelController,
            ShowPanelLink = isSuperAdmin || canReadProducts || canReadCompanyProfile,
            CatalogActive = isHome && (string.Equals(action, "Index", StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, "Details", StringComparison.OrdinalIgnoreCase)),
            OriginsActive = isHome && string.Equals(action, "Origenes", StringComparison.OrdinalIgnoreCase),
            AboutActive = isHome && string.Equals(action, "Nosotros", StringComparison.OrdinalIgnoreCase),
            WholesaleActive = string.Equals(controller, "Account", StringComparison.OrdinalIgnoreCase)
                && string.Equals(action, "RegisterProvider", StringComparison.OrdinalIgnoreCase)
        });
    }
}

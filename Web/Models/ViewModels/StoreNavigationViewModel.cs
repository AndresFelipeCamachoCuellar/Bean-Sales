namespace Web.Models.ViewModels;

public sealed class StoreNavigationViewModel
{
    public bool IsSignedIn { get; init; }
    public bool IsSuperAdmin { get; init; }
    public bool IsProvider { get; init; }
    public bool CanReadUsers { get; init; }
    public bool CanReadRoles { get; init; }
    public bool CanReadApprovals { get; init; }
    public bool CanReadProducts { get; init; }
    public bool CanReadCompanyProfile { get; init; }
    public int PendingApprovals { get; init; }
    public int CartCount { get; init; }
    public string DisplayName { get; init; } = "Mi cuenta";
    public string Initial { get; init; } = "U";
    public string RoleLabel { get; init; } = "Cliente";
    public string PanelController { get; init; } = "Home";
    public bool ShowPanelLink { get; init; }
    public bool CatalogActive { get; init; }
    public bool OriginsActive { get; init; }
    public bool AboutActive { get; init; }
    public bool WholesaleActive { get; init; }
}

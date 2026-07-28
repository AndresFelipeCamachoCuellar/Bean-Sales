namespace Web.Models.ViewModels;

public class ManagePermissionsViewModel
{
    public string RoleId { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public List<ModulePermissionsViewModel> Modules { get; set; } = new();
}

public class ModulePermissionsViewModel
{
    public string ModuleName { get; set; } = string.Empty;
    public string ModuleCode { get; set; } = string.Empty;
    public List<PermissionSelectionViewModel> Permissions { get; set; } = new();
}

public class PermissionSelectionViewModel
{
    public Guid ParametricPermissionID { get; set; }
    public string PermissionName { get; set; } = string.Empty; // e.g., "Create", "Read"
    public string PermissionCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Selected { get; set; }
}

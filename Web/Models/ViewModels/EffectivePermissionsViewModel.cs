namespace Web.Models.ViewModels;

/// <summary>
/// "¿Qué puede hacer REALMENTE este usuario, y por qué?" (épica E5, HU-5.5).
///
/// Hasta ahora responder eso exigía cruzar a mano cuatro tablas en la BD
/// (<c>AspNetUserRoles</c> → <c>Permissions</c> → <c>ParametricPermissions</c> →
/// <c>ParametricModules</c>). Esta pantalla lo resuelve de un vistazo y, sobre todo,
/// muestra DE QUÉ ROL viene cada permiso: sin esa columna, quitarle un permiso a alguien
/// es adivinar cuál de sus roles hay que tocar.
///
/// Es de solo lectura: no concede ni revoca nada. Los permisos se siguen editando en
/// Roles/ManagePermissions (Bean) y CompanyRoles/ManagePermissions (proveedor).
/// </summary>
public class EffectivePermissionsViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;

    /// <summary>Empresa proveedora del usuario, o null si es staff de Bean.</summary>
    public string? ProviderName { get; set; }

    public bool IsProvider => !string.IsNullOrEmpty(ProviderName);

    /// <summary>Roles asignados al usuario (el origen de TODO lo demás).</summary>
    public List<EffectiveRoleViewModel> Roles { get; set; } = new();

    /// <summary>Permisos efectivos agrupados por módulo.</summary>
    public List<EffectiveModuleViewModel> Modules { get; set; } = new();

    public int TotalPermissions => Modules.Sum(m => m.Permissions.Count);
}

public class EffectiveRoleViewModel
{
    public Guid RoleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>null = rol global de Bean; con valor = rol propio de una empresa proveedora.</summary>
    public Guid? ProviderID { get; set; }

    public bool IsGlobal => ProviderID == null;

    /// <summary>Cuántos permisos aporta este rol al total efectivo.</summary>
    public int PermissionCount { get; set; }
}

public class EffectiveModuleViewModel
{
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public List<EffectivePermissionViewModel> Permissions { get; set; } = new();
}

public class EffectivePermissionViewModel
{
    public string PermissionCode { get; set; } = string.Empty;
    public string PermissionName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Roles que conceden ESTE permiso. Casi siempre es uno, pero puede haber varios:
    /// ahí está justamente el valor de la pantalla — revocarlo exige tocarlos todos.
    /// </summary>
    public List<string> GrantedBy { get; set; } = new();
}

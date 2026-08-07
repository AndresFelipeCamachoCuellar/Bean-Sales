namespace Web.Constants;

public static class Modules
{
    public const string Users = "Users";
    public const string Roles = "Roles";
    public const string Security = "Security";
    public const string CompanyProfile = "CompanyProfile";
    public const string CompanyUsers = "CompanyUsers";
    public const string CompanyRoles = "CompanyRoles";
    public const string Products = "Products";
    public const string ProductApprovals = "ProductApprovals";
    public const string Orders = "Orders";

    // Módulos del ciclo BETA (ago 2026) — ver Docs/epicas_beta_2026-08.md (E5, HU-5.1).
    // Se siembran en ContextSeed.SeedPermissionsAsync; NO se asignan al rol de proveedor:
    // Pricing y Agreements exponen costo, margen y condiciones comerciales de Bean.
    public const string Inventory = "Inventory";
    public const string Warehouses = "Warehouses";
    public const string Pricing = "Pricing";
    public const string Agreements = "Agreements";
    public const string Settlements = "Settlements";
    public const string Notifications = "Notifications";
}

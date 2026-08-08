using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Web.Constants;
using Web.Data;
using Web.Models;
using Web.Models.Enums;
using Web.Services.Tenancy;

namespace Tests.Tenancy;

/// <summary>
/// Banco de pruebas del aislamiento multi-tenant: una base SQLite EN MEMORIA con el esquema
/// real de <see cref="ApplicationDbContext"/> y dos proveedores completos sembrados.
///
/// ─────────────────────────────────────────────────────────────────────────────────────
/// POR QUÉ SQLITE Y NO INMEMORY
/// ─────────────────────────────────────────────────────────────────────────────────────
/// InMemory no es una base de datos: no traduce LINQ a SQL. Un filtro multi-tenant que EF
/// no supiera traducir pasaría el test en verde y fallaría en producción — justo el error
/// que estos tests existen para evitar. SQLite ejecuta SQL real contra un esquema real.
///
/// Cada instancia abre su PROPIA conexión <c>:memory:</c>; la base vive mientras la
/// conexión esté abierta y se destruye al hacer <c>Dispose</c>. Los tests no se pisan entre
/// sí ni dependen del orden.
/// </summary>
public sealed class TenantTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public ApplicationDbContext Db { get; }

    // ---- Datos sembrados (los tests se leen mejor con nombres que con GUIDs) ----

    public Guid ProviderAId { get; } = Guid.NewGuid();
    public Guid ProviderBId { get; } = Guid.NewGuid();

    public Guid UserAId { get; } = Guid.NewGuid();
    public Guid UserBId { get; } = Guid.NewGuid();
    public Guid StaffId { get; } = Guid.NewGuid();
    public Guid CustomerId { get; } = Guid.NewGuid();

    public Guid ProductAId { get; } = Guid.NewGuid();
    public Guid ProductBId { get; } = Guid.NewGuid();

    public Guid RoleAId { get; } = Guid.NewGuid();
    public Guid RoleBId { get; } = Guid.NewGuid();
    public Guid GlobalRoleId { get; } = Guid.NewGuid();

    /// <summary>Pedido pagado que solo contiene café de A.</summary>
    public Guid OrderOnlyAId { get; } = Guid.NewGuid();

    /// <summary>Pedido pagado que solo contiene café de B.</summary>
    public Guid OrderOnlyBId { get; } = Guid.NewGuid();

    /// <summary>Carrito multi-marca: una línea de A y una de B en el MISMO pedido.</summary>
    public Guid OrderMixedId { get; } = Guid.NewGuid();

    /// <summary>Ámbito de un usuario de la empresa A.</summary>
    public ProviderScopeSnapshot ScopeA => ProviderScopeSnapshot.ForProvider(UserAId, ProviderAId);

    /// <summary>Ámbito de un usuario de la empresa B.</summary>
    public ProviderScopeSnapshot ScopeB => ProviderScopeSnapshot.ForProvider(UserBId, ProviderBId);

    /// <summary>Ámbito de un usuario interno de Bean (sin ProviderID): alcance global.</summary>
    public ProviderScopeSnapshot ScopeStaff => ProviderScopeSnapshot.ForStaff(StaffId);

    public TenantTestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .EnableSensitiveDataLogging()
            .Options;

        Db = new ApplicationDbContext(options);
        Db.Database.EnsureCreated();

        Seed();
    }

    private void Seed()
    {
        var country = new Country
        {
            CountryID = Guid.NewGuid(),
            Name = "Colombia",
            Status = true,
            CreatedBy = "TEST",
            CreatedOn = DateTime.Now
        };

        var documentType = new DocumentType
        {
            DocumentTypeID = Guid.NewGuid(),
            Name = "Cédula",
            Status = true,
            CreatedBy = "TEST",
            CreatedOn = DateTime.Now
        };

        Db.Countries.Add(country);
        Db.DocumentTypes.Add(documentType);

        Db.Providers.AddRange(
            NewProvider(ProviderAId, "Finca La Esperanza", "900111111"),
            NewProvider(ProviderBId, "Café del Cauca", "900222222"));

        Db.Users.AddRange(
            NewUser(UserAId, "admin@esperanza.co", ProviderAId, country, documentType),
            NewUser(UserBId, "admin@cauca.co", ProviderBId, country, documentType),
            NewUser(StaffId, "staff@bean.co", null, country, documentType),
            NewUser(CustomerId, "cliente@gmail.com", null, country, documentType));

        Db.Roles.AddRange(
            // Las DOS empresas llaman "Bodeguero" a su rol: es el caso que antes reventaba
            // contra el índice único global de Identity.
            NewRole(RoleAId, "Bodeguero", ProviderAId),
            NewRole(RoleBId, "Bodeguero", ProviderBId),
            // Rol global de Bean: ProviderID null y nombre de Identity SIN prefijar, porque
            // se usa por nombre en [Authorize(Roles = ...)]. NINGÚN proveedor debe verlo en
            // su pantalla de "Roles (Empresa)".
            NewRole(GlobalRoleId, Roles.SuperAdmin, null));

        Db.Products.AddRange(
            NewProduct(ProductAId, ProviderAId, "Geisha de la Esperanza", price: 60_000m, supplierPrice: 40_000m),
            NewProduct(ProductBId, ProviderBId, "Caturra del Cauca", price: 48_000m, supplierPrice: 30_000m));

        Db.Orders.AddRange(
            NewOrder(OrderOnlyAId, CustomerId, "Pedido solo A"),
            NewOrder(OrderOnlyBId, CustomerId, "Pedido solo B"),
            NewOrder(OrderMixedId, CustomerId, "Pedido mixto"));

        Db.OrderItems.AddRange(
            NewItem(OrderOnlyAId, ProductAId, "Geisha de la Esperanza", unit: 60_000m, cost: 40_000m, qty: 2),
            NewItem(OrderOnlyBId, ProductBId, "Caturra del Cauca", unit: 48_000m, cost: 30_000m, qty: 1),
            // El pedido mixto es el caso interesante: los DOS proveedores lo ven, pero cada
            // uno solo puede leer su propia línea y su propio pago.
            NewItem(OrderMixedId, ProductAId, "Geisha de la Esperanza", unit: 60_000m, cost: 40_000m, qty: 1),
            NewItem(OrderMixedId, ProductBId, "Caturra del Cauca", unit: 48_000m, cost: 30_000m, qty: 3));

        Db.SaveChanges();
        Db.ChangeTracker.Clear();
    }

    // ------------------------------------------------------------------- Factorías

    private static Provider NewProvider(Guid id, string name, string nit) => new()
    {
        ProviderID = id,
        Name = name,
        NIT = nit,
        Address = "Calle 1",
        Phone = "3000000000",
        Email = $"{nit}@test.co",
        ApprovalStatus = ApprovalStatus.Approved,
        Status = true,
        CreatedBy = "TEST",
        CreatedOn = DateTime.Now
    };

    private static ApplicationUser NewUser(
        Guid id, string email, Guid? providerId, Country country, DocumentType documentType) => new()
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            FirstName = "Nombre",
            LastName = "Apellido",
            Gender = "Not Specified",
            DocumentTypeID = documentType.DocumentTypeID,
            DocumentNumber = Guid.NewGuid().ToString("N")[..10],
            CountryID = country.CountryID,
            ProviderID = providerId,
            Status = true,
            CreatedBy = "TEST",
            CreatedOn = DateTime.Now
        };

    /// <summary>
    /// Crea un rol con la MISMA convención que la aplicación: el nombre visible va en
    /// <c>DisplayName</c> y el de Identity se prefija con el proveedor —salvo en los roles
    /// globales de Bean, donde ambos coinciden. Reproducir esto en el banco de pruebas es lo
    /// que permite que dos empresas se llamen igual sin chocar contra el índice único.
    /// </summary>
    private static ApplicationRole NewRole(Guid id, string displayName, Guid? providerId)
    {
        var identityName = providerId.HasValue
            ? RoleNaming.ForProvider(providerId.Value, displayName)
            : displayName;

        return new ApplicationRole
        {
            Id = id,
            Name = identityName,
            NormalizedName = identityName.ToUpperInvariant(),
            DisplayName = displayName,
            Description = displayName,
            ProviderID = providerId,
            Status = true,
            CreatedBy = "TEST",
            CreatedOn = DateTime.Now
        };
    }

    private static Product NewProduct(Guid id, Guid providerId, string name, decimal price, decimal supplierPrice) => new()
    {
        ProductID = id,
        ProviderID = providerId,
        Name = name,
        Description = name,
        Price = price,
        SupplierPrice = supplierPrice,
        Stock = 100,
        ProductStatus = ProductStatus.Active,
        Status = true,
        CreatedBy = "TEST",
        CreatedOn = DateTime.Now
    };

    private static Order NewOrder(Guid id, Guid userId, string label) => new()
    {
        OrderID = id,
        UserID = userId,
        OrderDate = DateTime.Now,
        OrderStatus = OrderStatus.Confirmed,
        // Los pedidos del proveedor solo listan los PAGADOS.
        PaymentStatus = PaymentStatus.Approved,
        FirstName = label,
        LastName = "Cliente",
        Address = "Calle Falsa 123",
        ShippingCountry = "Colombia",
        PaymentMethod = "WOMPI",
        CurrencyCode = "COP",
        Subtotal = 0m,
        ShippingCost = 0m,
        TotalAmount = 0m
    };

    private static OrderItem NewItem(
        Guid orderId, Guid productId, string productName, decimal unit, decimal cost, int qty) => new()
        {
            OrderItemID = Guid.NewGuid(),
            OrderID = orderId,
            ProductID = productId,
            ProductName = productName,
            UnitPrice = unit,
            SupplierPriceSnapshot = cost,
            Quantity = qty,
            SubTotal = unit * qty
        };

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}

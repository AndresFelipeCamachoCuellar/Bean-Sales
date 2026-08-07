using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Web.Models;

namespace Web.Data;

// Full customization of IdentityDbContext to include ApplicationRole and ApplicationUserRole
public class ApplicationDbContext : IdentityDbContext<
    ApplicationUser, 
    ApplicationRole, 
    Guid, 
    IdentityUserClaim<Guid>, 
    ApplicationUserRole, 
    IdentityUserLogin<Guid>, 
    IdentityRoleClaim<Guid>, 
    IdentityUserToken<Guid>>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Country> Countries { get; set; }
    public DbSet<DocumentType> DocumentTypes { get; set; }
    public DbSet<Provider> Providers { get; set; }
    
    // Custom Security Core
    public DbSet<ParametricModule> ParametricModules { get; set; }
    public DbSet<ParametricPermission> ParametricPermissions { get; set; }
    public DbSet<Permission> Permissions { get; set; }
    
    // Product Workflow
    public DbSet<Product> Products { get; set; }
    public DbSet<ProductCountry> ProductCountries { get; set; }
    public DbSet<ProductImage> ProductImages { get; set; }
    public DbSet<ShoppingCartItem> ShoppingCartItems { get; set; }

    // Sales Cycle
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }

    // Shipping
    public DbSet<ShippingCity> ShippingCities { get; set; }

    // Pricing (E2 · ciclo BETA ago-2026)
    public DbSet<PricingSettings> PricingSettings { get; set; }
    public DbSet<PriceChangeLog> PriceChangeLogs { get; set; }

    // Inventario multi-bodega (E1 · ciclo BETA ago-2026)
    public DbSet<Warehouse> Warehouses { get; set; }
    public DbSet<StockItem> StockItems { get; set; }
    public DbSet<StockMovement> StockMovements { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Map standard identity tables to match user preference if needed, 
        // OR simply just extend them. 
        // Here we configure the customized relationship for ApplicationUserRole
        
        builder.Entity<ApplicationUserRole>(b =>
        {
            // Identity defaults to Composite Key (UserId, RoleId). 
            // We must respect this for Identity methods to work.
            b.HasKey(ur => new { ur.UserId, ur.RoleId });
            
            // Map table name to AspNetUserRoles (or standard adaptation)
            b.ToTable("AspNetUserRoles");
        });
        
        // Ensure ParametricModule Code is unique
        builder.Entity<ParametricModule>()
            .HasIndex(m => m.Code)
            .IsUnique();

        // Ensure ParametricPermission Code is unique
        // Ensure ParametricPermission Code is unique per Module
        builder.Entity<ParametricPermission>()
            .HasIndex(p => new { p.ModuleID, p.Code })
            .IsUnique();

        // Product Workflow Configurations
        builder.Entity<ProductCountry>()
            .HasKey(pc => new { pc.ProductID, pc.CountryID });

        builder.Entity<ProductCountry>()
            .HasOne(pc => pc.Product)
            .WithMany(p => p.ProductCountries)
            .HasForeignKey(pc => pc.ProductID);

        builder.Entity<ProductCountry>()
            .HasOne(pc => pc.Country)
            .WithMany(c => c.ProductCountries) // Need to add this to Country.cs
            .HasForeignKey(pc => pc.CountryID);

        // Galería de fotos: si algún día se borra DURO un producto, sus fotos se van con
        // él. No introduce "multiple cascade paths" porque ProductImage no tiene otra FK.
        // ⚠️ La cascada limpia la BD pero NO Cloudinary: para eso existe
        // ProductImageService.DeleteAllForProductAsync, que hay que llamar explícitamente.
        builder.Entity<ProductImage>()
            .HasOne(i => i.Product)
            .WithMany(p => p.Images)
            .HasForeignKey(i => i.ProductID)
            .OnDelete(DeleteBehavior.Cascade);

        // La consulta de la galería es SIEMPRE "por producto, ordenado".
        builder.Entity<ProductImage>()
            .HasIndex(i => new { i.ProductID, i.SortOrder });

        // El public_id identifica el recurso en Cloudinary: duplicarlo sería un doble
        // registro (dos filas apuntando al mismo archivo). Siempre viene con valor,
        // así que el índice no necesita filtro.
        builder.Entity<ProductImage>()
            .HasIndex(i => i.PublicId)
            .IsUnique();

        // Sales Cycle Configurations
        // Order -> User (no borrar pedidos si se borra el usuario: histórico de ventas)
        builder.Entity<Order>()
            .HasOne(o => o.User)
            .WithMany()
            .HasForeignKey(o => o.UserID)
            .OnDelete(DeleteBehavior.Restrict);

        // OrderItem -> Order (borrar líneas si se borra el pedido)
        builder.Entity<OrderItem>()
            .HasOne(oi => oi.Order)
            .WithMany(o => o.Items)
            .HasForeignKey(oi => oi.OrderID)
            .OnDelete(DeleteBehavior.Cascade);

        // OrderItem -> Product (conservar histórico aunque se borre el producto)
        builder.Entity<OrderItem>()
            .HasOne(oi => oi.Product)
            .WithMany()
            .HasForeignKey(oi => oi.ProductID)
            .OnDelete(DeleteBehavior.Restrict);

        // Pagos: el webhook de Wompi llega SIN sesión y solo trae la referencia,
        // así que se busca el pedido por PaymentReference (índice único: la referencia
        // no se puede repetir en Wompi). Filtrado para no chocar con los pedidos
        // históricos, que tienen la columna en NULL.
        builder.Entity<Order>()
            .HasIndex(o => o.PaymentReference)
            .IsUnique()
            .HasFilter("[PaymentReference] IS NOT NULL");

        // KPIs del dashboard y barrido de reservas vencidas.
        builder.Entity<Order>()
            .HasIndex(o => o.PaymentStatus);

        // Shipping: el código DANE identifica al municipio, no puede repetirse.
        builder.Entity<ShippingCity>()
            .HasIndex(c => c.DaneCode)
            .IsUnique();

        // ---------------------------------------------------------------------
        //  Pricing (E2 · ciclo BETA ago-2026)
        // ---------------------------------------------------------------------

        // Histórico de precios de un lote. Cascade: si algún día se borra DURO un
        // producto, su histórico se va con él (no tiene otra FK, así que no introduce
        // "multiple cascade paths").
        builder.Entity<PriceChangeLog>()
            .HasOne(l => l.Product)
            .WithMany(p => p.PriceChanges)
            .HasForeignKey(l => l.ProductID)
            .OnDelete(DeleteBehavior.Cascade);

        // La consulta es SIEMPRE "histórico de este producto, del más reciente al más viejo".
        builder.Entity<PriceChangeLog>()
            .HasIndex(l => new { l.ProductID, l.ChangedOn });

        // Bandeja "Márgenes por revisar": se filtra por la bandera, no se escanea la tabla.
        builder.Entity<Product>()
            .HasIndex(p => p.MarginAlert);

        // ---------------------------------------------------------------------
        //  Inventario multi-bodega (E1 · ciclo BETA ago-2026)
        // ---------------------------------------------------------------------

        // El código de bodega es su identidad operativa: no se puede repetir.
        builder.Entity<Warehouse>()
            .HasIndex(w => w.Code)
            .IsUnique();

        // Cotizar el envío DESDE la bodega necesita resolver su DANE con frecuencia.
        builder.Entity<Warehouse>()
            .HasIndex(w => w.DaneCode);

        // Warehouse -> Country en Restrict: un país con bodegas no se borra en silencio.
        builder.Entity<Warehouse>()
            .HasOne(w => w.Country)
            .WithMany()
            .HasForeignKey(w => w.CountryID)
            .OnDelete(DeleteBehavior.Restrict);

        // StockItem: saldo por producto × bodega.
        builder.Entity<StockItem>()
            .HasKey(s => new { s.ProductID, s.WarehouseID });

        // ⚠️ "Multiple cascade paths": Product y Warehouse llegan ambos a StockItem, y
        // Warehouse llega además por Country. Se usa Restrict en las dos ramas, el mismo
        // criterio que ya resolvió esto en Order→User y OrderItem→Product.
        builder.Entity<StockItem>()
            .HasOne(s => s.Product)
            .WithMany(p => p.StockItems)
            .HasForeignKey(s => s.ProductID)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StockItem>()
            .HasOne(s => s.Warehouse)
            .WithMany(w => w.StockItems)
            .HasForeignKey(s => s.WarehouseID)
            .OnDelete(DeleteBehavior.Restrict);

        // Libro mayor. Restrict en ambas FK: un asiento contable NO se borra en cascada,
        // nunca, por definición.
        builder.Entity<StockMovement>()
            .HasOne(m => m.Product)
            .WithMany()
            .HasForeignKey(m => m.ProductID)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StockMovement>()
            .HasOne(m => m.Warehouse)
            .WithMany()
            .HasForeignKey(m => m.WarehouseID)
            .OnDelete(DeleteBehavior.Restrict);

        // La consulta es SIEMPRE "movimientos de este producto en esta bodega, por fecha".
        // El hosting tiene 256 MB y esta tabla crece sin techo: sin este índice, el
        // historial haría table scan a los pocos meses.
        builder.Entity<StockMovement>()
            .HasIndex(m => new { m.ProductID, m.WarehouseID, m.CreatedOn });

        // Trazabilidad inversa: "qué movió este pedido / esta recepción".
        builder.Entity<StockMovement>()
            .HasIndex(m => new { m.ReferenceType, m.ReferenceID });
    }
}

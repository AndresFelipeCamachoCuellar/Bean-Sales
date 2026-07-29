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
    public DbSet<ShoppingCartItem> ShoppingCartItems { get; set; }

    // Sales Cycle
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }

    // Shipping
    public DbSet<ShippingCity> ShippingCities { get; set; }

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
    }
}

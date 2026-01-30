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
    }
}

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
    
    // Custom Security Core
    public DbSet<ParametricModule> ParametricModules { get; set; }
    public DbSet<ParametricPermission> ParametricPermissions { get; set; }
    public DbSet<Permission> Permissions { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Map standard identity tables to match user preference if needed, 
        // OR simply just extend them. 
        // Here we configure the customized relationship for ApplicationUserRole
        
        builder.Entity<ApplicationUserRole>(b =>
        {
            // Identity defaults to Composite Key (UserId, RoleId). 
            // User requested UserRoleID as PK.
            // We override the Key.
            b.HasKey(ur => ur.UserRoleID);
            
            // Map table name to AspNetUserRoles (or standard adaptation)
            b.ToTable("AspNetUserRoles");
        });
        
        // Ensure ParametricModule Code is unique
        builder.Entity<ParametricModule>()
            .HasIndex(m => m.Code)
            .IsUnique();

        // Ensure ParametricPermission Code is unique
        builder.Entity<ParametricPermission>()
            .HasIndex(p => p.Code)
            .IsUnique();
    }
}

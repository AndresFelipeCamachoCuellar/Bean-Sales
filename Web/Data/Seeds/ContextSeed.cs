using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Web.Constants;
using Web.Models;

namespace Web.Data.Seeds;

public static class ContextSeed
{
    public static async Task SeedCountriesAsync(ApplicationDbContext context)
    {
        // Check if any countries exist
        if (!await context.Countries.AnyAsync())
        {
            var countries = new List<Country>
            {
                new Country 
                { 
                    Name = "Colombia", 
                    Status = true, 
                    CreatedBy = "SYSTEM", 
                    CreatedOn = DateTime.Now 
                },
                new Country 
                { 
                    Name = "Canada", 
                    Status = true, 
                    CreatedBy = "SYSTEM", 
                    CreatedOn = DateTime.Now 
                }
            };
            await context.Countries.AddRangeAsync(countries);
            await context.SaveChangesAsync();
        }
    }

    public static async Task SeedDocumentTypesAsync(ApplicationDbContext context)
    {
        // Check if any document types exist
        if (!await context.DocumentTypes.AnyAsync())
        {
            var documentTypes = new List<DocumentType>
            {
                new DocumentType 
                { 
                    Name = "Cédula de Ciudadanía", 
                    Status = true, 
                    CreatedBy = "SYSTEM", 
                    CreatedOn = DateTime.Now 
                },
                new DocumentType 
                { 
                    Name = "NIT", 
                    Status = true, 
                    CreatedBy = "SYSTEM", 
                    CreatedOn = DateTime.Now 
                },
                new DocumentType 
                { 
                    Name = "Cédula de Extranjería", 
                    Status = true, 
                    CreatedBy = "SYSTEM", 
                    CreatedOn = DateTime.Now 
                }
            };
            await context.DocumentTypes.AddRangeAsync(documentTypes);
            await context.SaveChangesAsync();
        }
    }

    public static async Task SeedRolesAsync(UserManager<ApplicationUser> userManager, RoleManager<ApplicationRole> roleManager)
    {
        //Seed Roles
        await roleManager.CreateAsync(new ApplicationRole 
        { 
            Name = Roles.SuperAdmin, 
            Description = "Full access to system",
            Status = true,
            CreatedBy = "SYSTEM",
            CreatedOn = DateTime.Now
        });
        await roleManager.CreateAsync(new ApplicationRole 
        { 
            Name = Roles.Admin, 
            Description = "Administrative access",
            Status = true,
            CreatedBy = "SYSTEM",
            CreatedOn = DateTime.Now
        });
        await roleManager.CreateAsync(new ApplicationRole 
        { 
            Name = Roles.Basic, 
            Description = "Standard user access",
            Status = true,
            CreatedBy = "SYSTEM",
            CreatedOn = DateTime.Now
        });
    }
}

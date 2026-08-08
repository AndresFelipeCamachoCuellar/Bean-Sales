using System.Text.Json;
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

    /// <summary>
    /// Catálogo de municipios (DIVIPOLA) usado como destino de envío. Se lee del JSON
    /// que se copia junto al binario. Es idempotente y degrada en silencio: si el
    /// archivo no está, la app arranca igual (el checkout usa la tarifa de respaldo).
    /// </summary>
    public static async Task SeedShippingCitiesAsync(ApplicationDbContext context, IWebHostEnvironment env)
    {
        if (await context.ShippingCities.AnyAsync()) return;

        var candidatePaths = new[]
        {
            Path.Combine(env.ContentRootPath, "Data", "Seeds", "Data", "colombia-cities.json"),
            Path.Combine(AppContext.BaseDirectory, "Data", "Seeds", "Data", "colombia-cities.json")
        };

        var path = candidatePaths.FirstOrDefault(File.Exists);
        if (path == null) return; // Degradación segura: sin catálogo, pero la app levanta.

        List<ShippingCitySeedRow>? rows;
        try
        {
            var json = await File.ReadAllTextAsync(path);
            rows = JsonSerializer.Deserialize<List<ShippingCitySeedRow>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception)
        {
            // JSON corrupto o ilegible: no se puede bloquear el arranque por el catálogo.
            return;
        }

        if (rows == null || rows.Count == 0) return;

        var cities = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.DaneCode) && !string.IsNullOrWhiteSpace(r.Name))
            .GroupBy(r => r.DaneCode!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(r => new ShippingCity
            {
                ShippingCityID = Guid.NewGuid(),
                DaneCode = r.DaneCode!.Trim(),
                Name = r.Name!.Trim(),
                Department = (r.Department ?? string.Empty).Trim(),
                Status = true,
                CreatedOn = DateTime.Now
            })
            .ToList();

        if (cities.Count == 0) return;

        await context.ShippingCities.AddRangeAsync(cities);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Configuración de márgenes (E2). RED DE SEGURIDAD: la migración <c>AddPricing</c> ya
    /// inserta la fila única, pero en una base creada desde cero por otra vía haría falta.
    /// Idempotente: si ya hay una fila, no toca nada.
    /// </summary>
    public static async Task SeedPricingSettingsAsync(ApplicationDbContext context)
    {
        if (await context.PricingSettings.AnyAsync()) return;

        // Valores del PO (07-ago-2026): objetivo 30 %, mínimo 15 %, redondeo 50 COP.
        context.PricingSettings.Add(new PricingSettings
        {
            PricingSettingsID = PricingSettings.SingletonId,
            TargetMarginPercent = 30m,
            MinimumMarginPercent = 15m,
            RoundingStep = 50m,
            Status = true,
            CreatedBy = "SYSTEM",
            CreatedOn = DateTime.Now
        });

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Bodega inicial CAL-01 (E1). RED DE SEGURIDAD para bases NUEVAS: las migraciones
    /// corren antes que este seed, así que cuando <c>AddWarehouseInventory</c> se aplica
    /// todavía no existe el país "Colombia" y su bloque de seed no puede crearla.
    /// Usa el MISMO GUID que la migración, así que las dos rutas convergen.
    ///
    /// ⚠️ DANE de Cali = 76001 (760001 es el código POSTAL).
    /// </summary>
    public static async Task SeedDefaultWarehouseAsync(ApplicationDbContext context)
    {
        if (await context.Warehouses.AnyAsync()) return;

        var colombia = await context.Countries.FirstOrDefaultAsync(c => c.Name == "Colombia");
        if (colombia == null) return; // Degradación segura: sin país no se puede crear.

        context.Warehouses.Add(new Warehouse
        {
            WarehouseID = Warehouse.DefaultWarehouseId,
            Code = Warehouse.DefaultWarehouseCode,
            Name = "Bodega Principal Cali",
            CountryID = colombia.CountryID,
            City = "Cali",
            DaneCode = "76001",
            Address = "Por definir",
            IsDefault = true,
            IsActive = true,
            Status = true,
            CreatedBy = "SYSTEM",
            CreatedOn = DateTime.Now
        });

        await context.SaveChangesAsync();
    }

    /// <summary>Fila del JSON de municipios (daneCode / name / department).</summary>
    private sealed class ShippingCitySeedRow
    {
        public string? DaneCode { get; set; }
        public string? Name { get; set; }
        public string? Department { get; set; }
    }

    /// <summary>
    /// Roles GLOBALES de Bean. Los cuatro tienen <c>ProviderID == null</c> y su
    /// <c>Name</c> es el literal de <see cref="Roles"/>: se usan por nombre en
    /// <c>[Authorize(Roles = ...)]</c>, <c>User.IsInRole(...)</c> y <c>AddToRoleAsync(...)</c>.
    /// <b>Nunca se prefijan</b> (ver <see cref="Web.Services.Tenancy.RoleNaming"/>); el prefijo
    /// por tenant es exclusivo de los roles creados desde <c>CompanyRoles</c>.
    /// </summary>
    public static async Task SeedRolesAsync(UserManager<ApplicationUser> userManager, RoleManager<ApplicationRole> roleManager)
    {
        //Seed Roles
        await roleManager.CreateAsync(new ApplicationRole
        {
            Name = Roles.SuperAdmin,
            DisplayName = Roles.SuperAdmin,
            Description = "Full access to system",
            Status = true,
            CreatedBy = "SYSTEM",
            CreatedOn = DateTime.Now
        });
        await roleManager.CreateAsync(new ApplicationRole
        {
            Name = Roles.Admin,
            DisplayName = Roles.Admin,
            Description = "Administrative access",
            Status = true,
            CreatedBy = "SYSTEM",
            CreatedOn = DateTime.Now
        });
        await roleManager.CreateAsync(new ApplicationRole
        {
            Name = Roles.Basic,
            DisplayName = Roles.Basic,
            Description = "Standard user access",
            Status = true,
            CreatedBy = "SYSTEM",
        });

        if (!await roleManager.RoleExistsAsync(Roles.ProviderAdmin))
        {
            await roleManager.CreateAsync(new ApplicationRole
            {
                Name = Roles.ProviderAdmin,
                DisplayName = Roles.ProviderAdmin,
                Description = "Administrator for a specific Provider Company",
                Status = true,
                CreatedBy = "SYSTEM",
                CreatedOn = DateTime.Now
            });
        }
    }

    public static async Task SeedSuperAdminAsync(UserManager<ApplicationUser> userManager, ApplicationDbContext context)
    {
        //Check if SuperAdmin user exists
        var superUser = new ApplicationUser
        {
            UserName = "andres.camacho",
            Email = "andres.felipe.camacho@outlook.com",
            FirstName = "Andres Felipe",
            LastName = "Camacho Cuellar",
            DocumentNumber = "1107524526",
            EmailConfirmed = true,
            PhoneNumberConfirmed = true,
            Status = true,
            CreatedBy = "SYSTEM",
            CreatedOn = DateTime.Now,
            Gender = "Male" // Defaulting, user didn't specify
        };

        if (userManager.Users.All(u => u.DocumentNumber != superUser.DocumentNumber))
        {
            var country = await context.Countries.FirstOrDefaultAsync(c => c.Name == "Colombia");
            var docType = await context.DocumentTypes.FirstOrDefaultAsync(d => d.Name == "Cédula de Ciudadanía");

            if (country != null && docType != null)
            {
                superUser.CountryID = country.CountryID;
                superUser.DocumentTypeID = docType.DocumentTypeID;
                
                // UserName/Email are set in initializer

                var result = await userManager.CreateAsync(superUser, "Andipipe1*");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(superUser, Roles.SuperAdmin);
                }
            }
        }

        // Logic to ensure roles are assigned even if user already exists
        var existingUser = await userManager.FindByEmailAsync(superUser.Email);
        if (existingUser != null)
        {
            if (!await userManager.IsInRoleAsync(existingUser, Roles.SuperAdmin))
            {
                await userManager.AddToRoleAsync(existingUser, Roles.SuperAdmin);
            }
        }
    }

    public static async Task SeedPermissionsAsync(ApplicationDbContext context, RoleManager<ApplicationRole> roleManager)
    {
        // 1. Seed Modules
        var modules = new[]
        {
            Modules.Users,
            Modules.Roles,
            Modules.CompanyProfile,
            Modules.CompanyUsers,
            Modules.CompanyRoles,
            Modules.Products,
            Modules.ProductApprovals,
            Modules.Orders,
            // Ciclo BETA (ago 2026). Al agregar módulos aquí basta reiniciar la app:
            // el bloque 3 asigna automáticamente TODOS los permisos al SuperAdmin.
            // Ojo: NO se asignan al ProviderAdmin (bloques 4-7 son explícitos por módulo).
            Modules.Inventory,
            Modules.Warehouses,
            Modules.Pricing,
            Modules.Agreements,
            Modules.Settlements,
            Modules.Notifications
        };
        foreach (var moduleCode in modules)
        {
            if (!await context.ParametricModules.AnyAsync(m => m.Code == moduleCode))
            {
                string moduleName = moduleCode switch
                {
                    Modules.Users => "Usuarios",
                    Modules.Roles => "Roles",
                    Modules.CompanyProfile => "Perfil de Empresa",
                    Modules.CompanyUsers => "Usuarios (Empresa)",
                    Modules.CompanyRoles => "Roles (Empresa)",
                    Modules.Products => "Productos (Gestión)",
                    Modules.ProductApprovals => "Aprobación de Productos",
                    Modules.Orders => "Pedidos",
                    Modules.Inventory => "Inventario",
                    Modules.Warehouses => "Bodegas",
                    Modules.Pricing => "Precios",
                    Modules.Agreements => "Acuerdos con proveedor",
                    Modules.Settlements => "Liquidaciones",
                    Modules.Notifications => "Notificaciones",
                    _ => moduleCode
                };

                await context.ParametricModules.AddAsync(new ParametricModule
                {
                    Name = moduleName,
                    Code = moduleCode,
                    Status = true,
                    CreatedBy = "SYSTEM",
                    CreatedOn = DateTime.Now
                });
            }
        }
        await context.SaveChangesAsync();

        // 2. Seed Parametric Permissions
        var allModules = await context.ParametricModules.ToListAsync();
        var permissions = new List<string> { Permissions.Create, Permissions.Read, Permissions.Update, Permissions.Delete };

        foreach (var module in allModules)
        {
            foreach (var permCode in permissions)
            {
                if (!await context.ParametricPermissions.AnyAsync(p => p.ModuleID == module.ModuleID && p.Code == permCode))
                {
                    await context.ParametricPermissions.AddAsync(new ParametricPermission
                    {
                        Name = permCode,
                        Code = permCode,
                        Description = $"Permite {permCode} en el módulo {module.Name}",
                        ModuleID = module.ModuleID,
                        Status = true,
                        CreatedBy = "SYSTEM",
                        CreatedOn = DateTime.Now
                    });
                }
            }
        }
        await context.SaveChangesAsync();

        // 3. Assign Permissions to SuperAdmin Role
        var superAdminRole = await roleManager.FindByNameAsync(Roles.SuperAdmin);
        if (superAdminRole != null)
        {
            var allPermissions = await context.ParametricPermissions.ToListAsync();
            foreach (var perm in allPermissions)
            {
                if (!await context.Permissions.AnyAsync(p => p.RoleID == superAdminRole.Id && p.ParametricPermissionID == perm.ParametricPermissionID))
                {
                    await context.Permissions.AddAsync(new Permission
                    {
                        PermissionID = Guid.NewGuid(),
                        RoleID = superAdminRole.Id,
                        ParametricPermissionID = perm.ParametricPermissionID,
                        Status = true,
                        CreatedBy = "SYSTEM",
                        CreatedOn = DateTime.Now
                    });
                }
            }
            await context.SaveChangesAsync();
        }

        // 4. Assign Permissions to ProviderAdmin Role (Company Profile)
        var providerAdminRole = await roleManager.FindByNameAsync(Roles.ProviderAdmin);
        var companyProfileModule = await context.ParametricModules.FirstOrDefaultAsync(m => m.Code == Modules.CompanyProfile);
        
        if (providerAdminRole != null && companyProfileModule != null)
        {
            var providerPermissions = new[] { Permissions.Read, Permissions.Update };
            var permsToAssign = await context.ParametricPermissions
                .Where(p => p.ModuleID == companyProfileModule.ModuleID && providerPermissions.Contains(p.Code))
                .ToListAsync();

            foreach (var perm in permsToAssign)
            {
                if (!await context.Permissions.AnyAsync(p => p.RoleID == providerAdminRole.Id && p.ParametricPermissionID == perm.ParametricPermissionID))
                {
                     await context.Permissions.AddAsync(new Permission
                     {
                         PermissionID = Guid.NewGuid(),
                         RoleID = providerAdminRole.Id,
                         ParametricPermissionID = perm.ParametricPermissionID,
                         Status = true,
                         CreatedBy = "SYSTEM",
                         CreatedOn = DateTime.Now
                     });
                }
            }
            await context.SaveChangesAsync();
        }

        // 5. Assign Permissions to ProviderAdmin Role (Company Users)
        var companyUsersModule = await context.ParametricModules.FirstOrDefaultAsync(m => m.Code == Modules.CompanyUsers);
        
        if (providerAdminRole != null && companyUsersModule != null)
        {
            var providerPermissions = new[] { Permissions.Create, Permissions.Read, Permissions.Update, Permissions.Delete };
            var permsToAssign = await context.ParametricPermissions
                .Where(p => p.ModuleID == companyUsersModule.ModuleID && providerPermissions.Contains(p.Code))
                .ToListAsync();

            foreach (var perm in permsToAssign)
            {
                if (!await context.Permissions.AnyAsync(p => p.RoleID == providerAdminRole.Id && p.ParametricPermissionID == perm.ParametricPermissionID))
                {
                     await context.Permissions.AddAsync(new Permission
                     {
                         PermissionID = Guid.NewGuid(),
                         RoleID = providerAdminRole.Id,
                         ParametricPermissionID = perm.ParametricPermissionID,
                         Status = true,
                         CreatedBy = "SYSTEM",
                         CreatedOn = DateTime.Now
                     });
                }
            }
            await context.SaveChangesAsync();
        }

        // 6. Assign Permissions to ProviderAdmin Role (Company Roles)
        var companyRolesModule = await context.ParametricModules.FirstOrDefaultAsync(m => m.Code == Modules.CompanyRoles);
        
        if (providerAdminRole != null && companyRolesModule != null)
        {
            var providerPermissions = new[] { Permissions.Create, Permissions.Read, Permissions.Update, Permissions.Delete };
            var permsToAssign = await context.ParametricPermissions
                .Where(p => p.ModuleID == companyRolesModule.ModuleID && providerPermissions.Contains(p.Code))
                .ToListAsync();

            foreach (var perm in permsToAssign)
            {
                if (!await context.Permissions.AnyAsync(p => p.RoleID == providerAdminRole.Id && p.ParametricPermissionID == perm.ParametricPermissionID))
                {
                     await context.Permissions.AddAsync(new Permission
                     {
                         PermissionID = Guid.NewGuid(),
                         RoleID = providerAdminRole.Id,
                         ParametricPermissionID = perm.ParametricPermissionID,
                         Status = true,
                         CreatedBy = "SYSTEM",
                         CreatedOn = DateTime.Now
                     });
                }
            }
            await context.SaveChangesAsync();
        }

        // 7. Assign Permissions to ProviderAdmin Role (Products)
        var productsModule = await context.ParametricModules.FirstOrDefaultAsync(m => m.Code == Modules.Products);
        
        if (providerAdminRole != null && productsModule != null)
        {
             // Provider Admin can Create, Read, Update, Delete (Drafts) products
            var providerPermissions = new[] { Permissions.Create, Permissions.Read, Permissions.Update, Permissions.Delete };
            var permsToAssign = await context.ParametricPermissions
                .Where(p => p.ModuleID == productsModule.ModuleID && providerPermissions.Contains(p.Code))
                .ToListAsync();

            foreach (var perm in permsToAssign)
            {
                if (!await context.Permissions.AnyAsync(p => p.RoleID == providerAdminRole.Id && p.ParametricPermissionID == perm.ParametricPermissionID))
                {
                     await context.Permissions.AddAsync(new Permission
                     {
                         PermissionID = Guid.NewGuid(),
                         RoleID = providerAdminRole.Id,
                         ParametricPermissionID = perm.ParametricPermissionID,
                         Status = true,
                         CreatedBy = "SYSTEM",
                         CreatedOn = DateTime.Now
                     });
                }
            }
            await context.SaveChangesAsync();
        }
    }
}

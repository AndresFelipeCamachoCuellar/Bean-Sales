using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Web.Data;
using Web.Data.Seeds;
using Web.Models;
using Web.Services.Shipping;

var builder = WebApplication.CreateBuilder(args);


// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<Web.Services.IPermissionService, Web.Services.PermissionService>();

// ---------- Envío ----------
// Caché en memoria: la usan el catálogo de municipios y (más adelante) las cotizaciones.
builder.Services.AddMemoryCache();

builder.Services.Configure<ShippingOptions>(builder.Configuration.GetSection("Shipping"));

// La tarifa plana se registra siempre: es el proveedor por defecto y, cuando exista
// el cotizador real, será además su red de seguridad (fallback).
builder.Services.AddScoped<FixedShippingQuoteService>();

builder.Services.AddScoped<IShippingQuoteService>(sp =>
{
    var shippingOptions = sp.GetRequiredService<IOptions<ShippingOptions>>().Value;
    var provider = shippingOptions.Provider;

    if (!string.Equals(provider, "Fixed", StringComparison.OrdinalIgnoreCase))
    {
        // La implementación de Mipaquete llega en la siguiente tanda. Mientras tanto
        // no se rompe el checkout: se cotiza con la tarifa plana y se deja rastro.
        sp.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Web.Services.Shipping")
            .LogWarning(
                "Shipping:Provider = '{Provider}' no tiene implementación disponible; se usa la tarifa fija de respaldo.",
                provider);
    }

    return sp.GetRequiredService<FixedShippingQuoteService>();
});

builder.Services.AddScoped<IShippingCityService, ShippingCityService>();


builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddControllersWithViews();

builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// Seed Data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<ApplicationDbContext>();
    // Ensure database is created/migrated.
    // MonsterASP no ejecuta `dotnet ef` en el host, por lo que aplicamos las
    // migraciones pendientes al iniciar la app (crea/actualiza el esquema solo).
    await context.Database.MigrateAsync();

    await ContextSeed.SeedCountriesAsync(context);
    await ContextSeed.SeedDocumentTypesAsync(context);
    await ContextSeed.SeedShippingCitiesAsync(context, app.Environment);

    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
    await ContextSeed.SeedRolesAsync(userManager, roleManager);
    await ContextSeed.SeedSuperAdminAsync(userManager, context);
    await ContextSeed.SeedPermissionsAsync(context, roleManager);
}

app.Run();

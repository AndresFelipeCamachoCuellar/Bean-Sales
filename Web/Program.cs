using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Web.Data;
using Web.Data.Seeds;
using Web.Models;
using Web.Services.Inventory;
using Web.Services.Media;
using Web.Services.Payments;
using Web.Services.Pricing;
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

// La tarifa plana se registra siempre: es el proveedor por defecto y, además,
// la red de seguridad (fallback) del cotizador real.
builder.Services.AddScoped<FixedShippingQuoteService>();

// El toggle se lee UNA vez, en el arranque: cambiar de proveedor exige reiniciar la
// app (en MonsterASP cambiar la config ya recicla el app pool). Evita quedarse a
// medio checkout con dos proveedores distintos.
var shippingProvider = builder.Configuration["Shipping:Provider"];
var mipaqueteRequested = string.Equals(shippingProvider, "Mipaquete", StringComparison.OrdinalIgnoreCase);

// Sin credencial no tiene sentido levantar el cotizador real: cotizaría, fallaría y
// degradaría en CADA checkout (una llamada HTTP perdida por pedido). Se decide una vez
// aquí y se deja rastro explícito en el log del host.
// ⚠️ La credencial del endpoint de cotización es la CUSTOMER KEY (header "customer-key",
// el UUID del comercio), NO el JWT: contrato real validado en vivo (jul-2026).
var mipaqueteCustomerKey = builder.Configuration["Shipping:Mipaquete:CustomerKey"];
var mipaqueteHasCustomerKey = !string.IsNullOrWhiteSpace(mipaqueteCustomerKey);
var useMipaquete = mipaqueteRequested && mipaqueteHasCustomerKey;

if (useMipaquete)
{
    // Typed client: evita el agotamiento de sockets de `new HttpClient()` y maneja bien el DNS.
    // El timeout real por intento lo aplica el servicio con un CancellationTokenSource;
    // aquí se deja un margen para que el corte lo controle él y no el HttpClient.
    var shippingTimeoutSeconds = builder.Configuration.GetValue<int?>("Shipping:Mipaquete:TimeoutSeconds") ?? 4;
    if (shippingTimeoutSeconds <= 0) shippingTimeoutSeconds = 4;

    builder.Services.AddHttpClient<MipaqueteShippingQuoteService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(shippingTimeoutSeconds + 5);
    });
}

builder.Services.AddScoped<IShippingQuoteService>(sp =>
{
    // La decisión ya está tomada arriba; aquí solo se entrega. El diagnóstico se
    // registra UNA vez al arrancar (ver más abajo) y no en cada petición.
    if (useMipaquete)
    {
        return sp.GetRequiredService<MipaqueteShippingQuoteService>();
    }

    return sp.GetRequiredService<FixedShippingQuoteService>();
});

builder.Services.AddScoped<IShippingCityService, ShippingCityService>();

// ---------- Pagos (Wompi · Web Checkout) ----------
// Los secretos NO viven en appsettings.json: llegan de appsettings.Development.json
// (gitignored) o inyectados por el pipeline desde GitHub Secrets.
builder.Services.Configure<WompiOptions>(builder.Configuration.GetSection("Wompi"));

var wompiTimeoutSeconds = builder.Configuration.GetValue<int?>("Wompi:TimeoutSeconds") ?? 8;
if (wompiTimeoutSeconds <= 0) wompiTimeoutSeconds = 8;

// Typed client: evita el agotamiento de sockets y maneja bien el DNS. El corte real
// por intento lo aplica el servicio con un CancellationTokenSource.
builder.Services.AddHttpClient<IPaymentGateway, WompiPaymentGateway>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(wompiTimeoutSeconds + 5);
});

builder.Services.AddScoped<PaymentApplicationService>();
builder.Services.AddScoped<PendingOrderExpirationService>();

// ---------- Imágenes de producto (Cloudinary · subida directa firmada) ----------
// El archivo NUNCA pasa por nuestro proceso: el navegador lo sube directo al CDN con un
// ticket que firmamos aquí. Con 256 MB de RAM y app pool de 32 bits, bufferear multipart
// era la peor decisión posible. Los secretos NO viven en appsettings.json commiteado.
builder.Services.Configure<CloudinaryOptions>(builder.Configuration.GetSection("Cloudinary"));

// La decisión se toma UNA vez, en el arranque (igual que el proveedor de envío): sin las
// 3 credenciales se registra la implementación deshabilitada y el sitio funciona
// exactamente como antes de este incremento.
var cloudinaryEnabled = builder.Configuration.GetValue<bool?>("Cloudinary:Enabled") ?? true;
var cloudinaryConfigured = cloudinaryEnabled
    && CloudinaryOptions.HasValue(builder.Configuration["Cloudinary:CloudName"])
    && CloudinaryOptions.HasValue(builder.Configuration["Cloudinary:ApiKey"])
    && CloudinaryOptions.HasValue(builder.Configuration["Cloudinary:ApiSecret"]);

if (cloudinaryConfigured)
{
    var cloudinaryDeleteTimeout = builder.Configuration.GetValue<int?>("Cloudinary:DeleteTimeoutSeconds") ?? 6;
    if (cloudinaryDeleteTimeout <= 0) cloudinaryDeleteTimeout = 6;

    // Typed client: solo se usa para borrar recursos (POST /image/destroy).
    builder.Services.AddHttpClient<IProductImageStorage, CloudinaryImageStorage>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(cloudinaryDeleteTimeout + 5);
    });
}
else
{
    builder.Services.AddScoped<IProductImageStorage, DisabledImageStorage>();
}

builder.Services.AddScoped<ProductImageService>();

// ---------- Pricing e inventario (ciclo BETA ago-2026) ----------
// PricingService cachea la configuración de márgenes en IMemoryCache (ya registrado
// arriba). InventoryService es el ÚNICO punto autorizado a escribir stock.
builder.Services.AddScoped<PricingService>();
builder.Services.AddScoped<InventoryService>();


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

// ---------- Diagnóstico del proveedor de envío (una sola vez, al arrancar) ----------
if (useMipaquete)
{
    app.Logger.LogInformation("Cotización de envío: mipaquete.com. Respaldo: tarifa fija.");
}
else if (mipaqueteRequested)
{
    // Provider = Mipaquete pero sin customer key: falta cargar la credencial en el host.
    app.Logger.LogWarning(
        "Shipping:Provider = 'Mipaquete' pero Shipping:Mipaquete:CustomerKey está vacía: se cotiza con la " +
        "tarifa fija. Carga la credencial (secret MIPAQUETE_CUSTOMER_KEY) en el host y reinicia la app.");
}
else if (!string.Equals(shippingProvider, "Fixed", StringComparison.OrdinalIgnoreCase)
         && !string.IsNullOrWhiteSpace(shippingProvider))
{
    app.Logger.LogWarning(
        "Shipping:Provider = '{Provider}' no tiene implementación disponible; se usa la tarifa fija de respaldo.",
        shippingProvider);
}

// ---------- Diagnóstico de la pasarela de pagos (una sola vez, al arrancar) ----------
// NUNCA se registra el valor de una llave: solo si está presente.
{
    var wompi = app.Services.GetRequiredService<IOptions<WompiOptions>>().Value;

    if (!wompi.Enabled)
    {
        app.Logger.LogWarning(
            "Pagos DESACTIVADOS (Wompi:Enabled = false): el checkout confirma pedidos sin cobrar.");
    }
    else if (!wompi.CanCharge)
    {
        app.Logger.LogWarning(
            "Pagos NO configurados (falta Wompi:PublicKey y/o Wompi:IntegritySecret): el checkout " +
            "confirma pedidos sin cobrar. Carga las llaves en el host y reinicia la app.");
    }
    else
    {
        app.Logger.LogInformation(
            "Pagos: Wompi Web Checkout, ambiente '{Ambiente}'. Secreto de eventos {Eventos}. " +
            "Llave privada {Privada}. Reserva de pago: {Minutos} min.",
            wompi.EventEnvironment,
            wompi.CanValidateEvents ? "presente" : "AUSENTE (el webhook responderá 503)",
            WompiOptions.HasValue(wompi.PrivateKey) ? "presente" : "ausente",
            wompi.ExpirationMinutesOrDefault);

        // Mezclar ambientes es el error más caro de esta integración: se avisa fuerte.
        var esperado = wompi.EventEnvironment == "prod" ? "pub_prod_" : "pub_test_";
        if (!wompi.PublicKey.StartsWith(esperado, StringComparison.Ordinal))
        {
            app.Logger.LogError(
                "La llave pública de Wompi no corresponde al ambiente configurado ('{Ambiente}': se esperaba " +
                "el prefijo {Prefijo}). Revisa Wompi:Environment y las llaves antes de cobrar.",
                wompi.EventEnvironment, esperado);
        }
    }
}

// ---------- Diagnóstico de la galería de fotos (una sola vez, al arrancar) ----------
// NUNCA se registra el valor de una credencial: solo si está presente.
{
    var cloudinary = app.Services.GetRequiredService<IOptions<CloudinaryOptions>>().Value;

    if (cloudinaryConfigured)
    {
        app.Logger.LogInformation(
            "Imágenes de producto: Cloudinary '{CloudName}', carpeta '{Carpeta}', máx {Fotos} fotos de {MB} MB " +
            "(mínimo {Min}px, formatos {Formatos}).",
            cloudinary.CloudName, cloudinary.FolderOrDefault, cloudinary.MaxImagesOrDefault,
            cloudinary.MaxFileSizeMb, cloudinary.MinDimensionOrDefault, cloudinary.AllowedFormatsCsv);
    }
    else if (!cloudinaryEnabled)
    {
        app.Logger.LogWarning(
            "Galería de fotos DESACTIVADA (Cloudinary:Enabled = false): el gestor queda en modo lectura " +
            "y el catálogo usa el patrón de respaldo.");
    }
    else
    {
        app.Logger.LogWarning(
            "Imágenes de producto NO configuradas (faltan Cloudinary:CloudName / ApiKey / ApiSecret): el gestor " +
            "de fotos queda en modo lectura y el catálogo usa el patrón de respaldo. Carga las credenciales en " +
            "el host y reinicia la app.");
    }
}

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

// Rutas por ATRIBUTO. Las usan el webhook de Wompi (/pagos/wompi/eventos) y el retorno
// del cliente (/pagos/resultado/{orderId}), que necesitan URLs estables y ajenas al
// patrón {controller}/{action}/{id}. Comparte el mismo data source que MapControllerRoute,
// así que no duplica endpoints ni cambia el comportamiento de las rutas convencionales.
app.MapControllers();

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
    // Ciclo BETA (ago-2026). Ambos son redes de seguridad idempotentes: las migraciones
    // AddPricing / AddWarehouseInventory ya siembran estas filas en bases existentes, pero
    // en una base NUEVA las migraciones corren antes de que existan los países.
    await ContextSeed.SeedPricingSettingsAsync(context);
    await ContextSeed.SeedDefaultWarehouseAsync(context);

    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
    await ContextSeed.SeedRolesAsync(userManager, roleManager);
    await ContextSeed.SeedSuperAdminAsync(userManager, context);
    await ContextSeed.SeedPermissionsAsync(context, roleManager);
}

app.Run();

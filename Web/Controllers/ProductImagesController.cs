using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Web.Constants;
using Web.Data;
using Web.Models;
using Web.Models.Enums;
using Web.Services;
using Web.Services.Media;

namespace Web.Controllers;

/// <summary>
/// Gestor de fotos de producto. Todas las acciones de escritura son AJAX y devuelven
/// JSON; el archivo NUNCA pasa por aquí (el navegador lo sube directo a Cloudinary con
/// un ticket firmado por <c>Ticket</c>).
///
/// ─────────────────────────────────────────────────────────────────────────────
/// AUTORIZACIÓN DUAL (por eso NO se usa el atributo [HasPermission])
/// ─────────────────────────────────────────────────────────────────────────────
///  · Staff de Bean con <c>ProductApprovals/Update</c> → CUALQUIER producto, en
///    CUALQUIER estado. Es el requisito del PO: "cuando el lote llega a nosotros
///    deberíamos poder agregar fotos nuevas o modificar las que están".
///  · Proveedor con <c>Products/Update</c> → SOLO sus productos
///    (<c>ProviderID</c>) y SOLO mientras el lote sea editable (Draft o Rejected),
///    igual que ya hace <c>ProductsController.Edit</c>.
///
/// Regla derivada: una vez que el proveedor envía el lote a aprobación, las fotos las
/// gestiona Bean. Así un proveedor no puede cambiar las fotos de un producto ya
/// publicado sin revisión.
///
/// NO se creó un módulo de permisos nuevo a propósito: obligaría a reiniciar la app
/// para que el seed lo cree (lección del Incremento 12) sin ningún beneficio real.
/// </summary>
[Authorize]
public class ProductImagesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPermissionService _permissions;
    private readonly IProductImageStorage _storage;
    private readonly ProductImageService _images;
    private readonly CloudinaryOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ProductImagesController> _logger;

    public ProductImagesController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IPermissionService permissions,
        IProductImageStorage storage,
        ProductImageService images,
        IOptions<CloudinaryOptions> options,
        IMemoryCache cache,
        ILogger<ProductImagesController> logger)
    {
        _context = context;
        _userManager = userManager;
        _permissions = permissions;
        _storage = storage;
        _images = images;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    // ================================================================ Vista del admin

    /// <summary>
    /// Página dedicada del gestor. La usa el staff desde la bandeja de aprobaciones
    /// (botón "Fotos") y también sirve al proveedor si llega por URL directa: la
    /// autorización es la misma.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Manage(Guid id)
    {
        var (product, role, canEdit) = await AuthorizeAsync(id);

        if (product is null)
        {
            return Forbid();
        }

        var model = await _images.BuildManagerAsync(product, canEdit, role);

        ViewBag.Product = product;
        ViewBag.ReturnToApprovals = role == ImageUploader.Admin;

        return View(model);
    }

    // ================================================================ Endpoints AJAX

    /// <summary>
    /// Paso 1: emite el ticket FIRMADO con el que el navegador sube a Cloudinary.
    /// La firma es la llave, y la llave se entrega solo con el producto en la mano.
    /// El <c>api_secret</c> NUNCA sale de aquí.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ticket(Guid productId)
    {
        var (product, _, canEdit) = await AuthorizeAsync(productId);

        if (product is null || !canEdit)
        {
            return Json(new { ok = false, error = "No tienes permiso para cambiar las fotos de este lote." });
        }

        if (!_storage.CanUpload)
        {
            return Json(new { ok = false, error = "La galería de fotos aún no está configurada." });
        }

        // La cuota se revisa ANTES del rate limit: así un usuario que llegó al máximo no
        // gasta su cupo de tickets del minuto por un error de interfaz.
        var count = await _images.CountAsync(productId);
        if (count >= _storage.MaxImagesPerProduct)
        {
            return Json(new
            {
                ok = false,
                error = $"Llegaste al máximo de {_storage.MaxImagesPerProduct} fotos. Elimina una para subir otra."
            });
        }

        if (!TryConsumeRateLimit())
        {
            return Json(new { ok = false, error = "Demasiadas subidas seguidas. Espera un minuto e inténtalo de nuevo." });
        }

        var ticket = _storage.CreateUploadTicket(productId);

        return Json(new
        {
            ok = true,
            uploadUrl = ticket.UploadUrl,
            apiKey = ticket.ApiKey,
            timestamp = ticket.Timestamp,
            signature = ticket.Signature,
            publicId = ticket.PublicId,
            allowedFormats = ticket.AllowedFormats,
            uploadPreset = ticket.UploadPreset,
            maxBytes = ticket.MaxBytes
        });
    }

    /// <summary>
    /// Paso 2: el navegador reporta lo que devolvió Cloudinary y aquí se registra.
    /// Es donde se aplican las validaciones que el cliente no puede garantizar
    /// (pertenencia, firma de respuesta, formato, tamaño, resolución y cuota).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterImageRequest request)
    {
        if (request is null || request.ProductId == Guid.Empty)
        {
            return Json(new { ok = false, error = "Faltan datos de la foto." });
        }

        var (product, role, canEdit) = await AuthorizeAsync(request.ProductId);

        if (product is null || !canEdit)
        {
            return Json(new { ok = false, error = "No tienes permiso para cambiar las fotos de este lote." });
        }

        if (!_storage.CanUpload)
        {
            return Json(new { ok = false, error = "La galería de fotos aún no está configurada." });
        }

        var upload = new CloudinaryUploadResult(
            PublicId: request.PublicId?.Trim() ?? string.Empty,
            Version: request.Version,
            Format: request.Format,
            Width: request.Width,
            Height: request.Height,
            Bytes: request.Bytes,
            SecureUrl: request.SecureUrl,
            Signature: request.Signature);

        var result = await _images.RegisterAsync(
            request.ProductId, upload, role, User.Identity?.Name ?? "SYSTEM");

        return JsonResultOf(result);
    }

    /// <summary>Elimina una foto: soft delete en BD + destroy en Cloudinary.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid productId, Guid imageId)
    {
        var (product, _, canEdit) = await AuthorizeAsync(productId);

        if (product is null || !canEdit)
        {
            return Json(new { ok = false, error = "No tienes permiso para cambiar las fotos de este lote." });
        }

        var result = await _images.DeleteAsync(productId, imageId);
        return JsonResultOf(result);
    }

    /// <summary>Marca una foto como portada. Sincroniza <c>Product.ImageUrl</c>.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cover(Guid productId, Guid imageId)
    {
        var (product, _, canEdit) = await AuthorizeAsync(productId);

        if (product is null || !canEdit)
        {
            return Json(new { ok = false, error = "No tienes permiso para cambiar las fotos de este lote." });
        }

        var result = await _images.SetCoverAsync(productId, imageId);
        return JsonResultOf(result);
    }

    /// <summary>Reordena la galería. Recibe los ids en el orden deseado.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reorder(Guid productId, List<Guid> order)
    {
        var (product, _, canEdit) = await AuthorizeAsync(productId);

        if (product is null || !canEdit)
        {
            return Json(new { ok = false, error = "No tienes permiso para cambiar las fotos de este lote." });
        }

        var result = await _images.ReorderAsync(productId, order ?? new List<Guid>());
        return JsonResultOf(result);
    }

    /// <summary>Guarda el texto alternativo de una foto (accesibilidad y SEO).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Alt(Guid productId, Guid imageId, string? altText)
    {
        var (product, _, canEdit) = await AuthorizeAsync(productId);

        if (product is null || !canEdit)
        {
            return Json(new { ok = false, error = "No tienes permiso para cambiar las fotos de este lote." });
        }

        var result = await _images.SetAltTextAsync(productId, imageId, altText);
        return JsonResultOf(result);
    }

    // ================================================================ Helpers

    private IActionResult JsonResultOf(ProductImageResult result) =>
        Json(new
        {
            ok = result.Ok,
            error = result.Error,
            coverUrl = result.CoverUrl,
            images = result.Images
        });

    /// <summary>
    /// Autorización dual. Devuelve el producto y con qué sombrero actúa el usuario.
    /// <c>canEdit</c> es false cuando el usuario puede VER el gestor pero no tocarlo
    /// (hoy no ocurre: quien no puede editar recibe <c>product == null</c>; se mantiene
    /// como punto de extensión para un futuro permiso de solo lectura).
    /// </summary>
    private async Task<(Product? product, ImageUploader role, bool canEdit)> AuthorizeAsync(Guid productId)
    {
        if (productId == Guid.Empty)
        {
            return (null, ImageUploader.Provider, false);
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return (null, ImageUploader.Provider, false);
        }

        // Comparación Guid con Guid: nada de ToString() dentro de la consulta EF.
        // Se incluye el proveedor porque la vista Manage muestra su nombre en la cabecera.
        var product = await _context.Products
            .Include(p => p.Provider)
            .FirstOrDefaultAsync(p => p.ProductID == productId && p.Status);

        if (product is null)
        {
            return (null, ImageUploader.Provider, false);
        }

        // 1) Staff de Bean: cualquier producto, cualquier estado.
        if (await _permissions.HasPermissionAsync(user, Modules.ProductApprovals, Permissions.Update))
        {
            return (product, ImageUploader.Admin, true);
        }

        // 2) Proveedor dueño, solo mientras el lote sea editable.
        var editableByProvider = product.ProductStatus == ProductStatus.Draft
                                 || product.ProductStatus == ProductStatus.Rejected;

        if (user.ProviderID.HasValue
            && product.ProviderID == user.ProviderID.Value
            && editableByProvider
            && await _permissions.HasPermissionAsync(user, Modules.Products, Permissions.Update))
        {
            return (product, ImageUploader.Provider, true);
        }

        return (null, ImageUploader.Provider, false);
    }

    /// <summary>
    /// Rate limit por usuario en <see cref="IMemoryCache"/> (ya registrado en Program.cs):
    /// es la única defensa contra un usuario legítimo que decida quemar la cuota del plan
    /// Free. Ventana deslizante simple de un minuto; barata y suficiente para el tamaño
    /// del negocio.
    /// </summary>
    private bool TryConsumeRateLimit()
    {
        var userId = _userManager.GetUserId(User) ?? "anon";

        // La clave incluye el minuto UTC: al cambiar de minuto se empieza a contar de cero.
        var key = $"cloudinary-tickets:{userId}:{DateTime.UtcNow:yyyyMMddHHmm}";

        var used = _cache.TryGetValue(key, out int stored) ? stored : 0;
        var max = _options.MaxTicketsPerMinuteOrDefault;

        if (used >= max)
        {
            _logger.LogWarning("Rate limit de tickets de subida alcanzado por el usuario {UserId}.", userId);
            return false;
        }

        _cache.Set(key, used + 1, TimeSpan.FromMinutes(2));
        return true;
    }

    /// <summary>Cuerpo del POST de registro (lo arma <c>bean-media.js</c> con la respuesta de Cloudinary).</summary>
    public class RegisterImageRequest
    {
        public Guid ProductId { get; set; }
        public string? PublicId { get; set; }
        public long? Version { get; set; }
        public string? Format { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public long? Bytes { get; set; }
        public string? SecureUrl { get; set; }
        public string? Signature { get; set; }
    }
}

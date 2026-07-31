using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Web.Data;
using Web.Models;
using Web.Models.Enums;
using Web.Services.Payments;
using Web.Services.Shipping;
using System.Globalization;
using System.Text.Json;

namespace Web.Controllers;

public class CartController : Controller
{
    /// <summary>Formato de moneda del storefront: "$18.040" con cultura es-CO.</summary>
    private static readonly CultureInfo CoCulture = new("es-CO");

    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IShippingQuoteService _shippingQuoteService;
    private readonly IShippingCityService _shippingCityService;
    private readonly ShippingOptions _shippingOptions;
    private readonly IPaymentGateway _paymentGateway;
    private readonly PaymentApplicationService _paymentApplication;
    private readonly PendingOrderExpirationService _paymentExpiration;
    private readonly WompiOptions _wompiOptions;
    private readonly ILogger<CartController> _logger;

    public CartController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IShippingQuoteService shippingQuoteService,
        IShippingCityService shippingCityService,
        IOptions<ShippingOptions> shippingOptions,
        IPaymentGateway paymentGateway,
        PaymentApplicationService paymentApplication,
        PendingOrderExpirationService paymentExpiration,
        IOptions<WompiOptions> wompiOptions,
        ILogger<CartController> logger)
    {
        _context = context;
        _userManager = userManager;
        _shippingQuoteService = shippingQuoteService;
        _shippingCityService = shippingCityService;
        _shippingOptions = shippingOptions.Value;
        _paymentGateway = paymentGateway;
        _paymentApplication = paymentApplication;
        _paymentExpiration = paymentExpiration;
        _wompiOptions = wompiOptions.Value;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        var cartItems = await GetCartItemsAsync();

        await SetCartTotalsAsync(cartItems);

        return View(cartItems);
    }

    [HttpPost]
    public async Task<IActionResult> AddToCart(Guid productId, int quantity = 1)
    {
        var product = await _context.Products.FindAsync(productId);
        if (product == null || !product.Status) return NotFound();

        // 1. Authenticated User -> DB
        if (User.Identity?.IsAuthenticated == true)
        {
            var user = await _userManager.GetUserAsync(User);
            var cartItem = await _context.ShoppingCartItems
                .FirstOrDefaultAsync(c => c.UserID == user.Id && c.ProductID == productId);

            if (cartItem != null)
            {
                cartItem.Quantity += quantity;
            }
            else
            {
                // Verify Stock? product.Stock < quantity...
                
                cartItem = new ShoppingCartItem
                {
                    ShoppingCartItemID = Guid.NewGuid(),
                    UserID = user.Id,
                    ProductID = productId,
                    Quantity = quantity,
                    CreatedOn = DateTime.Now
                };
                _context.ShoppingCartItems.Add(cartItem);
            }
            await _context.SaveChangesAsync();
        }
        else // 2. Anonymous User -> Session
        {
            var sessionCart = GetSessionCart();
            var existingItem = sessionCart.FirstOrDefault(c => c.ProductID == productId);

            if (existingItem != null)
            {
                existingItem.Quantity += quantity;
            }
            else
            {
                sessionCart.Add(new ShoppingCartItem
                {
                    ShoppingCartItemID = Guid.NewGuid(),
                    ProductID = productId,
                    Quantity = quantity,
                    Product = product, // For display in session (serialized, but handle circular ref carefully)
                    // Actually, we shouldn't serialize full Product entity due to circular refs.
                    // We'll reload product details in GetCartItemsAsync.
                    CreatedOn = DateTime.Now
                });
            }
            SaveSessionCart(sessionCart);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> UpdateQuantity(Guid id, int quantity)
    {
        if (quantity < 1) quantity = 1; // Minimum 1

        if (User.Identity?.IsAuthenticated == true)
        {
            var user = await _userManager.GetUserAsync(User);
            var item = await _context.ShoppingCartItems
                .FirstOrDefaultAsync(c => c.ShoppingCartItemID == id && c.UserID == user.Id);
            
            if (item != null)
            {
                item.Quantity = quantity;
                await _context.SaveChangesAsync();
            }
        }
        else
        {
            var sessionCart = GetSessionCart();
            var item = sessionCart.FirstOrDefault(c => c.ShoppingCartItemID == id);
            if (item != null)
            {
                item.Quantity = quantity;
                SaveSessionCart(sessionCart);
            }
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Checkout(CancellationToken ct)
    {
        // 1. Validate Auth
        if (User.Identity?.IsAuthenticated != true)
        {
            // Redirect to Register (Client requirement)
            return RedirectToAction("Register", "Account", new { returnUrl = Url.Action("Checkout", "Cart") });
        }

        // 1b. Barrido perezoso de reservas de pago vencidas: justo antes de que alguien
        //     compre es cuando más importa que el stock abandonado esté liberado.
        //     Está acotado y con cooldown; nunca lanza.
        await _paymentExpiration.SweepAsync(ct);

        // 2. Validate Cart Content
        var cartItems = await GetCartItemsAsync();
        if (!cartItems.Any())
        {
            return RedirectToAction(nameof(Index));
        }

        // 3. Prellenar el formulario con el perfil real del cliente.
        //    (Antes se ponía User.Identity.Name en "Nombre", que es el email.)
        var user = await _userManager.GetUserAsync(User);

        // 3b. ¿Hay un pedido esperando pago? Se ofrece retomarlo en vez de duplicarlo.
        var pendiente = await FindLivePendingOrderAsync(user.Id, ct);
        if (pendiente != null)
        {
            ViewBag.PendingOrderId = pendiente.OrderID;
            ViewBag.PendingOrderShort = ShortId(pendiente.OrderID);
            ViewBag.PendingOrderMinutes = MinutesLeft(pendiente.PaymentExpiresAt);
        }

        ViewBag.FormFirstName = user?.FirstName ?? string.Empty;
        ViewBag.FormLastName = user?.LastName ?? string.Empty;
        ViewBag.FormAddress = string.Empty;
        ViewBag.FormZip = string.Empty;
        // Identity ya guarda el celular en AspNetUsers: si el cliente lo tiene, se prellena.
        ViewBag.FormPhone = user?.PhoneNumber ?? string.Empty;

        // 4. Show Checkout View (Summary, Address, Payment placeholder)
        await PrepareCheckoutViewAsync(cartItems, null, null, domestic: true);
        return View(cartItems);
    }

    /// <summary>
    /// Municipios de un departamento, para el select dependiente del checkout.
    /// Sale del catálogo local cacheado: no depende de la API de la transportadora.
    /// </summary>
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Cities(string? department, CancellationToken ct)
    {
        var cities = await _shippingCityService.GetCitiesAsync(department, ct);

        return Json(cities
            .Select(c => new { dane = c.DaneCode, name = c.Name, department = c.Department })
            .ToList());
    }

    /// <summary>
    /// Cotiza el envío del carrito ACTUAL del servidor hacia el municipio indicado.
    /// El cliente solo manda un identificador (código DANE): nunca un importe.
    /// Devuelve el resumen completo ya calculado y formateado para que el JS solo pinte.
    /// </summary>
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuoteShipping(string? daneCode, CancellationToken ct)
    {
        var cartItems = await GetCartItemsAsync();
        if (!cartItems.Any())
        {
            return Json(new
            {
                status = "Invalid",
                canCheckout = false,
                message = "Tu carrito está vacío."
            });
        }

        var city = await _shippingCityService.FindByDaneCodeAsync(daneCode, ct);
        if (city == null)
        {
            return Json(new
            {
                status = "Invalid",
                canCheckout = false,
                message = "Elige tu ciudad para calcular el costo del envío."
            });
        }

        // El endpoint AJAX solo existe para destinos nacionales (los únicos con DANE).
        var totals = await QuoteCartAsync(cartItems, city.DaneCode, domestic: true, ct: ct);
        var blocked = totals.Status is ShippingQuoteStatus.NoCoverage or ShippingQuoteStatus.Invalid;

        return Json(new
        {
            status = totals.Status.ToString(),
            source = totals.Source,
            message = totals.Message,
            canCheckout = !blocked,
            carrier = totals.CarrierName,
            // URL absoluta https ya validada en el servicio; hoy ninguna vista la pinta.
            carrierLogoUrl = totals.CarrierLogoUrl,
            days = totals.EstimatedDays,
            city = city.Name,
            subtotal = totals.Subtotal,
            shipping = blocked ? (decimal?)null : totals.Shipping,
            grandTotal = blocked ? (decimal?)null : totals.GrandTotal,
            subtotalText = Money(totals.Subtotal),
            shippingText = blocked ? "—" : Money(totals.Shipping),
            grandTotalText = blocked ? "—" : Money(totals.GrandTotal)
        });
    }

    /// <summary>
    /// Crea el pedido y lo manda a cobrar. Del destino solo se acepta el
    /// <paramref name="cityDaneCode"/>: el nombre de la ciudad y el costo del envío
    /// SIEMPRE se resuelven en el servidor (<paramref name="cityName"/> se recibe por el
    /// formulario pero se ignora a propósito).
    ///
    /// Con la pasarela activa el pedido nace en <see cref="OrderStatus.Pending"/> con el
    /// stock RESERVADO y se redirige al Web Checkout de Wompi; lo confirma el webhook.
    /// Sin llaves de Wompi cargadas se conserva el comportamiento anterior (pedido
    /// confirmado sin cobrar), para poder desplegar sin romper el sitio.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(string firstName, string lastName, string address,
        string country, string state, string zip, string? phone, string? paymentMethod,
        string? cityDaneCode, string? cityName, CancellationToken ct)
    {
        // 1. Validate Auth (mismo criterio que el GET)
        if (User.Identity?.IsAuthenticated != true)
        {
            return RedirectToAction("Register", "Account", new { returnUrl = Url.Action("Checkout", "Cart") });
        }

        var user = await _userManager.GetUserAsync(User);

        // 1b. Anti doble clic / doble envío del formulario: si el usuario ya tiene un
        //     pedido esperando pago, se retoma ese en vez de crear otro (que duplicaría
        //     la reserva de stock).
        var pendienteVigente = await FindLivePendingOrderAsync(user.Id, ct);
        if (pendienteVigente != null)
        {
            return RedirectToAction(nameof(Pay), new { id = pendienteVigente.OrderID });
        }

        // 2. Obtener carrito
        var cartItems = await GetCartItemsAsync();
        if (!cartItems.Any())
        {
            return RedirectToAction(nameof(Index));
        }

        // Se conserva lo que el cliente ya escribió para no perder el formulario si hay error.
        ViewBag.FormFirstName = firstName;
        ViewBag.FormLastName = lastName;
        ViewBag.FormAddress = address;
        ViewBag.FormZip = zip;
        ViewBag.FormPhone = phone;

        // El cotizador nacional (DANE + transportadoras colombianas) solo aplica a Colombia.
        // Fuera del país se cobra la tarifa de configuración y no se pide ciudad.
        var domestic = IsDomesticDestination(country);

        // 2b. Celular OBLIGATORIO. Wompi exige shipping-address:phone-number en cuanto se
        //     manda el bloque de dirección, y la transportadora lo necesita para entregar.
        //     Se normaliza con la MISMA regla que usa la pasarela: si ahí no pasa, aquí tampoco.
        var normalizedPhone = PaymentContact.NormalizePhone(phone);
        if (normalizedPhone is null)
        {
            TempData["CheckoutError"] = string.IsNullOrWhiteSpace(phone)
                ? "Escribe un celular de contacto: la transportadora lo necesita para coordinar la entrega."
                : "Ese celular no parece válido. Escríbelo con indicativo si es del exterior (ej. 3001234567).";
            await PrepareCheckoutViewAsync(cartItems, cityDaneCode, state, domestic, ct);
            return View(cartItems);
        }

        // 3. Validar stock por ítem
        var sinStock = cartItems
            .FirstOrDefault(i => i.Product == null || i.Product.Stock < i.Quantity);
        if (sinStock != null)
        {
            var nombre = sinStock.Product?.Name ?? "un producto";
            TempData["CheckoutError"] = $"No hay stock suficiente para \"{nombre}\". Ajusta la cantidad e inténtalo de nuevo.";
            await PrepareCheckoutViewAsync(cartItems, cityDaneCode, state, domestic, ct);
            return View(cartItems);
        }

        // 3b. Validar el destino contra el catálogo: el DANE no puede ser inventado.
        ShippingCityItem? city = null;
        if (domestic)
        {
            city = await _shippingCityService.FindByDaneCodeAsync(cityDaneCode, ct);
            if (city == null)
            {
                TempData["CheckoutError"] = "Selecciona el departamento y la ciudad de destino para calcular el envío.";
                await PrepareCheckoutViewAsync(cartItems, null, state, domestic, ct);
                return View(cartItems);
            }
        }

        // 3c. RE-COTIZAR EN EL SERVIDOR. El precio del envío NUNCA viene del formulario:
        //     si viajara en un input oculto, cualquiera lo pondría en 0 desde DevTools.
        //     Dentro del TTL de caché esto es un hit y devuelve exactamente lo que vio el cliente.
        var totals = await PrepareCheckoutViewAsync(cartItems, city?.DaneCode, state, domestic, ct);

        // 3d. Sin cobertura o carrito no despachable: aquí sí se bloquea (no se puede entregar).
        if (totals.Status is ShippingQuoteStatus.NoCoverage or ShippingQuoteStatus.Invalid)
        {
            var destino = city?.Name ?? "ese destino";
            TempData["CheckoutError"] = totals.Message
                ?? $"Todavía no llegamos a {destino}. Escríbenos y lo gestionamos contigo.";
            return View(cartItems);
        }

        // 4-7. Crear orden + descontar stock + vaciar carrito de forma atómica
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            // Con pasarela: el pedido nace pendiente de pago. Sin pasarela: comportamiento anterior.
            var cobrarConPasarela = _paymentGateway.CanCharge;

            var order = new Order
            {
                OrderID = Guid.NewGuid(),
                UserID = user.Id,
                OrderDate = DateTime.Now,
                OrderStatus = cobrarConPasarela ? OrderStatus.Pending : OrderStatus.Confirmed,
                FirstName = firstName,
                LastName = lastName,
                Address = address,
                State = state,
                ZipCode = zip,
                // Normalizado (solo dígitos): es lo que viaja a Wompi y a la transportadora.
                CustomerPhone = normalizedPhone,
                ShippingCountry = country,
                // El método REAL lo elige el cliente dentro de Wompi y llega por el webhook.
                PaymentMethod = cobrarConPasarela
                    ? "wompi"
                    : (string.IsNullOrWhiteSpace(paymentMethod) ? "manual" : paymentMethod),
                CurrencyCode = "COP",
                // Snapshot de la cotización de envío usada al cobrar.
                ShippingCity = city?.Name,
                ShippingCityDaneCode = city?.DaneCode,
                ShippingCarrier = totals.CarrierName,
                ShippingEstimatedDays = totals.EstimatedDays,
                ShippingQuoteSource = totals.Source
            };

            decimal subtotal = 0m;
            foreach (var item in cartItems)
            {
                var unitPrice = item.Product?.Price ?? 0m;
                var lineTotal = unitPrice * item.Quantity;
                subtotal += lineTotal;

                order.Items.Add(new OrderItem
                {
                    OrderItemID = Guid.NewGuid(),
                    OrderID = order.OrderID,
                    ProductID = item.ProductID,
                    ProductName = item.Product?.Name ?? string.Empty,
                    UnitPrice = unitPrice,
                    Quantity = item.Quantity,
                    SubTotal = lineTotal
                });

                // 5. Descontar stock (entidad trackeada por el contexto)
                var product = await _context.Products.FindAsync(item.ProductID);
                if (product != null)
                {
                    product.Stock -= item.Quantity;
                }
            }

            // Importes redondeados a PESOS ENTEROS antes de firmar y persistir: COP no
            // tiene decimales y, si la firma se calculara sobre un valor con centavos,
            // el monto firmado y el del pedido discreparían.
            order.Subtotal = PaymentAmounts.RoundPesos(subtotal);
            order.ShippingCost = PaymentAmounts.RoundPesos(totals.Shipping); // cotizado por IShippingQuoteService
            order.TotalAmount = order.Subtotal + order.ShippingCost;

            if (cobrarConPasarela)
            {
                order.PaymentStatus = PaymentStatus.Pending;
                order.PaymentAttempt = 1;
                order.PaymentReference = _paymentGateway.BuildReference(order.OrderID, 1);
                order.PaymentAmountInCents = PaymentAmounts.ToCents(order.TotalAmount);
                order.PaymentExpiresAt = DateTime.Now.AddMinutes(_wompiOptions.ExpirationMinutesOrDefault);
                order.PaymentEnvironment = _wompiOptions.EventEnvironment;
            }
            else
            {
                // Modo sin pasarela (kill switch / llaves sin cargar): el pedido se da por
                // pagado igual que antes, para no romper el sitio ni los KPIs.
                order.PaymentStatus = PaymentStatus.Approved;
                order.PaidAt = DateTime.Now;
            }

            _context.Orders.Add(order);

            // 6. Vaciar carrito del usuario
            var userCart = await _context.ShoppingCartItems
                .Where(c => c.UserID == user.Id)
                .ToListAsync(ct);
            _context.ShoppingCartItems.RemoveRange(userCart);

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            // 8. A cobrar (Web Checkout de Wompi) o directo a la confirmación.
            return cobrarConPasarela
                ? RedirectToAction(nameof(Pay), new { id = order.OrderID })
                : RedirectToAction(nameof(Confirmation), new { id = order.OrderID });
        }
        catch (Exception ex)
        {
            // Sin CancellationToken a propósito: si la petición se canceló, el rollback
            // y el re-render tienen que ocurrir igual.
            await transaction.RollbackAsync();

            // El detalle REAL queda en el log (nivel Error, con la excepción completa):
            // antes solo se veía el mensaje genérico en pantalla y no había forma de
            // diagnosticar nada. Aquí no entra ningún secreto: solo datos del pedido.
            _logger.LogError(ex,
                "Falló la creación del pedido del usuario {UserId}. Destino: {City} ({Dane}), " +
                "país {Country}, {ItemCount} ítem(s), envío {Shipping} ({Source}).",
                user.Id, city?.Name ?? "n/d", city?.DaneCode ?? "n/d", country,
                cartItems.Count, totals.Shipping, totals.Source);

            TempData["CheckoutError"] = ex is DbUpdateException
                ? "No pudimos guardar tu pedido (problema temporal con nuestra base de datos). " +
                  "Tu carrito sigue intacto: vuelve a intentarlo en un momento."
                : "No pudimos completar tu pedido. Ya registramos el detalle del error; " +
                  "tu carrito sigue intacto, inténtalo de nuevo o escríbenos si vuelve a pasar.";

            await PrepareCheckoutViewAsync(cartItems, city?.DaneCode, state, domestic);
            return View(cartItems);
        }
    }

    // =====================================================================
    //  PAGO (Wompi · Web Checkout por redirección)
    // =====================================================================

    /// <summary>
    /// Puente hacia la pasarela: renderiza el formulario firmado que hace GET a
    /// checkout.wompi.co. Es una vista y no un 302 a propósito, porque la codificación
    /// del parámetro literal <c>signature:integrity</c> en una query string armada a
    /// mano no está documentada; el &lt;form&gt; es la forma que Wompi publica.
    /// </summary>
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Pay(Guid id, CancellationToken ct)
    {
        var user = await _userManager.GetUserAsync(User);

        var order = await _context.Orders.FirstOrDefaultAsync(o => o.OrderID == id, ct);
        if (order == null) return NotFound();
        if (order.UserID != user.Id) return Forbid();

        // Nada que cobrar: ya se pagó, se canceló o la pasarela está apagada.
        if (!_paymentGateway.CanCharge
            || order.PaymentStatus == PaymentStatus.Approved
            || order.OrderStatus == OrderStatus.Cancelled)
        {
            return RedirectToAction(nameof(Confirmation), new { id = order.OrderID });
        }

        // La reserva venció: no se manda a pagar algo que ya no está garantizado.
        if (order.PaymentExpiresAt.HasValue && order.PaymentExpiresAt.Value <= DateTime.Now)
        {
            TempData["PaymentError"] = "El tiempo para completar el pago venció. Vuelve a armar tu pedido.";
            return RedirectToAction(nameof(Confirmation), new { id = order.OrderID });
        }

        // Un pedido creado antes de activar la pasarela puede no tener referencia.
        if (string.IsNullOrWhiteSpace(order.PaymentReference))
        {
            order.PaymentAttempt = order.PaymentAttempt < 1 ? 1 : order.PaymentAttempt;
            order.PaymentReference = _paymentGateway.BuildReference(order.OrderID, order.PaymentAttempt);
            order.PaymentAmountInCents ??= PaymentAmounts.ToCents(order.TotalAmount);
            order.PaymentExpiresAt ??= DateTime.Now.AddMinutes(_wompiOptions.ExpirationMinutesOrDefault);
            await _context.SaveChangesAsync(ct);
        }

        // Wompi añade "?id=<transaccion>" a esta URL al devolver al cliente.
        var redirectUrl = Url.Action(nameof(PaymentResult), "Cart",
            new { orderId = order.OrderID }, Request.Scheme) ?? string.Empty;

        // Armar el formulario firmado no debería fallar, pero si falla (configuración
        // incompleta, datos del pedido inconsistentes) el cliente NO puede quedarse en
        // blanco: se registra el detalle y se lo manda al detalle del pedido, donde
        // puede reintentar el pago o cancelar y liberar el stock.
        PaymentCheckoutRequest checkout;
        try
        {
            checkout = _paymentGateway.BuildCheckout(order, redirectUrl, user.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "No se pudo preparar el pago del pedido {OrderId} (intento {Attempt}, total {Total} {Currency}).",
                order.OrderID, order.PaymentAttempt, order.TotalAmount, order.CurrencyCode);

            TempData["PaymentError"] = "No pudimos iniciar el pago con la pasarela. " +
                "Tu pedido quedó reservado: vuelve a intentar el pago desde aquí.";
            return RedirectToAction(nameof(Confirmation), new { id = order.OrderID });
        }

        return View("PayRedirect", checkout);
    }

    /// <summary>
    /// Retorno del cliente desde Wompi. Es INFORMATIVO y, además, red de seguridad:
    /// se consulta el estado real por API (server-side) y se aplica con el mismo
    /// servicio idempotente que usa el webhook. La fuente de verdad sigue siendo el evento.
    ///
    /// La URL lleva nuestro OrderID en la ruta porque Wompi solo devuelve el id de SU
    /// transacción en la query string (<c>?id=...</c>); así, aunque la consulta al API
    /// falle, sabemos a qué pedido volver.
    /// </summary>
    [HttpGet("pagos/resultado/{orderId:guid}")]
    [Authorize]
    public async Task<IActionResult> PaymentResult(Guid orderId, string? id, CancellationToken ct)
    {
        var user = await _userManager.GetUserAsync(User);

        var order = await _context.Orders.FirstOrDefaultAsync(o => o.OrderID == orderId, ct);
        if (order == null) return NotFound();
        if (order.UserID != user.Id) return Forbid();

        if (!string.IsNullOrWhiteSpace(id) && _paymentGateway.CanCharge)
        {
            var snapshot = await _paymentGateway.GetTransactionAsync(id!, ct);
            if (snapshot is not null)
            {
                try
                {
                    await _paymentApplication.ApplyAsync(snapshot, ct);
                }
                catch (Exception)
                {
                    // Si algo falla aquí NO se le muestra un error al cliente: el webhook
                    // resolverá. La confirmación mostrará "estamos confirmando tu pago".
                }
            }
        }

        return RedirectToAction(nameof(Confirmation), new { id = orderId });
    }

    /// <summary>
    /// Reintento de pago tras un rechazo. Genera SIEMPRE una referencia nueva: la
    /// documentación solo garantiza que se puedan reutilizar referencias de
    /// transacciones no completadas, así que no se arriesga.
    /// </summary>
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RetryPayment(Guid id, CancellationToken ct)
    {
        var user = await _userManager.GetUserAsync(User);

        var order = await _context.Orders.FirstOrDefaultAsync(o => o.OrderID == id, ct);
        if (order == null) return NotFound();
        if (order.UserID != user.Id) return Forbid();

        if (!_paymentGateway.CanCharge
            || order.OrderStatus != OrderStatus.Pending
            || order.PaymentStatus == PaymentStatus.Approved)
        {
            return RedirectToAction(nameof(Confirmation), new { id });
        }

        order.PaymentAttempt = order.PaymentAttempt < 1 ? 2 : order.PaymentAttempt + 1;
        order.PaymentReference = _paymentGateway.BuildReference(order.OrderID, order.PaymentAttempt);
        order.PaymentStatus = PaymentStatus.Pending;
        order.PaymentTransactionId = null;
        order.PaymentStatusMessage = null;
        order.PaymentAmountInCents = PaymentAmounts.ToCents(order.TotalAmount);
        // Se renueva la ventana de la reserva: el cliente está intentando pagar ahora.
        order.PaymentExpiresAt = DateTime.Now.AddMinutes(_wompiOptions.ExpirationMinutesOrDefault);

        await _context.SaveChangesAsync(ct);

        return RedirectToAction(nameof(Pay), new { id = order.OrderID });
    }

    /// <summary>El cliente desiste: se cancela el pedido y se libera la reserva de stock.</summary>
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelPayment(Guid id, CancellationToken ct)
    {
        var user = await _userManager.GetUserAsync(User);

        var order = await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.OrderID == id, ct);

        if (order == null) return NotFound();
        if (order.UserID != user.Id) return Forbid();

        var cancelado = await _paymentApplication.CancelUnpaidAsync(order, ct);

        TempData["PaymentError"] = cancelado
            ? "Cancelamos tu pedido. Los lotes vuelven a estar disponibles."
            : "Este pedido ya no se puede cancelar desde aquí.";

        return RedirectToAction(nameof(Confirmation), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Confirmation(Guid id)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return RedirectToAction("Login", "Account");
        }

        var user = await _userManager.GetUserAsync(User);

        var order = await _context.Orders
            .Include(o => o.Items)
            .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(o => o.OrderID == id);

        if (order == null)
        {
            return NotFound();
        }

        // La orden debe pertenecer al usuario actual
        if (order.UserID != user.Id)
        {
            return Forbid();
        }

        // Datos de pago para la vista (4 estados: aprobado / procesando / rechazado / vencido).
        ViewBag.PaymentMinutesLeft = MinutesLeft(order.PaymentExpiresAt);
        ViewBag.CanRetryPayment = _paymentGateway.CanCharge
            && order.OrderStatus == OrderStatus.Pending
            && order.PaymentStatus != PaymentStatus.Approved
            && (order.PaymentExpiresAt == null || order.PaymentExpiresAt > DateTime.Now);

        return View(order);
    }

    [HttpPost]
    public async Task<IActionResult> Remove(Guid id)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var user = await _userManager.GetUserAsync(User);
            var item = await _context.ShoppingCartItems
                .FirstOrDefaultAsync(c => c.ShoppingCartItemID == id && c.UserID == user.Id);
            
            if (item != null)
            {
                _context.ShoppingCartItems.Remove(item);
                await _context.SaveChangesAsync();
            }
        }
        else
        {
            var sessionCart = GetSessionCart();
            var item = sessionCart.FirstOrDefault(c => c.ShoppingCartItemID == id);
            if (item != null)
            {
                sessionCart.Remove(item);
                SaveSessionCart(sessionCart);
            }
        }
        return RedirectToAction(nameof(Index));
    }

    // Helpers

    /// <summary>Totales del carrito ya cotizados, para vistas y para la orden.</summary>
    private sealed record CartTotals(
        decimal Subtotal,
        decimal Shipping,
        decimal GrandTotal,
        string? CarrierName,
        int? EstimatedDays,
        string Source,
        ShippingQuoteStatus Status,
        string? Message,
        // Logo de la transportadora, si el cotizador lo expone (la tarifa fija no).
        string? CarrierLogoUrl = null);

    /// <summary>Importe con el formato del storefront ("18.040"), sin el signo.</summary>
    private static string Money(decimal value) => value.ToString("#,##0", CoCulture);

    /// <summary>Identificador corto que ve el cliente ("#DCF46AC3").</summary>
    private static string ShortId(Guid id) => id.ToString("N").Substring(0, 8).ToUpperInvariant();

    /// <summary>Minutos que le quedan a la reserva de pago (null si no aplica o ya venció).</summary>
    private static int? MinutesLeft(DateTime? expiresAt)
    {
        if (expiresAt is null) return null;

        var minutes = (int)Math.Ceiling((expiresAt.Value - DateTime.Now).TotalMinutes);
        return minutes > 0 ? minutes : null;
    }

    /// <summary>
    /// Pedido del usuario que sigue esperando pago y cuya reserva NO ha vencido.
    /// Es el cinturón anti doble clic: se retoma en vez de crear otro pedido.
    /// </summary>
    private async Task<Order?> FindLivePendingOrderAsync(Guid userId, CancellationToken ct)
    {
        if (!_paymentGateway.CanCharge) return null;

        var now = DateTime.Now;

        return await _context.Orders
            .Where(o => o.UserID == userId
                        && o.OrderStatus == OrderStatus.Pending
                        && o.PaymentStatus != PaymentStatus.Approved
                        && o.PaymentExpiresAt != null
                        && o.PaymentExpiresAt > now)
            .OrderByDescending(o => o.OrderDate)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// true si el destino es Colombia (único país con cotización dinámica: el catálogo
    /// DANE y las transportadoras de mipaquete son nacionales). Vacío = Colombia, que es
    /// lo único que hoy ofrece el formulario.
    /// </summary>
    private static bool IsDomesticDestination(string? country)
    {
        if (string.IsNullOrWhiteSpace(country)) return true;

        var value = country.Trim();
        return value.Equals("Colombia", StringComparison.OrdinalIgnoreCase)
            || value.Equals("CO", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Cotiza el carrito hacia un destino SIN tocar ViewBag. La usan los endpoints
    /// que devuelven JSON.
    /// </summary>
    private async Task<CartTotals> QuoteCartAsync(
        IEnumerable<ShoppingCartItem> cartItems,
        string? destinationDaneCode,
        bool domestic = true,
        CancellationToken ct = default)
    {
        var items = cartItems as IList<ShoppingCartItem> ?? cartItems.ToList();

        var subtotal = items.Sum(i => i.Quantity * (i.Product?.Price ?? 0m));

        // Destino fuera de Colombia: no hay código DANE ni transportadora nacional que
        // cotizar. Se cobra la tarifa de configuración y la compra sigue su curso.
        if (!domestic)
        {
            return new CartTotals(
                subtotal,
                _shippingOptions.FallbackCost,
                subtotal + _shippingOptions.FallbackCost,
                FixedShippingQuoteService.CarrierDisplayName,
                _shippingOptions.FallbackDays,
                FixedShippingQuoteService.SourceName,
                ShippingQuoteStatus.Ok,
                null);
        }

        // Un solo bulto agregado con la heurística compartida (peso/dimensiones/valor declarado).
        var package = ShippingPackageBuilder.Build(
            items,
            _shippingOptions.Origin.DaneCode,
            destinationDaneCode ?? string.Empty,
            _shippingOptions.Defaults);

        // El carrito excede el peso máximo por bulto: no es despachable tal cual.
        if (package.ExceedsMaxWeight)
        {
            // Se deja la tarifa de configuración en los ViewBag para que ninguna vista
            // muestre "$0", pero el estado Invalid impide cerrar la compra.
            return new CartTotals(
                subtotal,
                _shippingOptions.FallbackCost,
                subtotal + _shippingOptions.FallbackCost,
                null, null, _shippingOptions.Provider,
                ShippingQuoteStatus.Invalid,
                "Tu pedido supera el peso máximo por envío. Escríbenos y cotizamos un despacho especial.");
        }

        var quote = await _shippingQuoteService.QuoteAsync(package.Request, ct);
        var cheapest = quote.Cheapest;

        // Sin opción cotizada se cae a la tarifa de configuración: el checkout nunca se queda sin precio.
        var shipping = cheapest?.Price ?? _shippingOptions.FallbackCost;

        return new CartTotals(
            subtotal,
            shipping,
            subtotal + shipping,
            cheapest?.CarrierName,
            cheapest?.EstimatedDays,
            quote.Source,
            quote.Status,
            quote.Message,
            cheapest?.LogoUrl);
    }

    /// <summary>
    /// Fuente ÚNICA de los totales del carrito: cotiza el envío y publica
    /// ViewBag.Total (subtotal), ViewBag.Shipping y ViewBag.GrandTotal.
    /// Todas las rutas que renderizan Cart/Index o Cart/Checkout pasan por aquí.
    /// </summary>
    private async Task<CartTotals> SetCartTotalsAsync(
        IEnumerable<ShoppingCartItem> cartItems,
        string? destinationDaneCode = null,
        bool domestic = true,
        CancellationToken ct = default)
    {
        var totals = await QuoteCartAsync(cartItems, destinationDaneCode, domestic, ct);

        ViewBag.Total = totals.Subtotal;         // subtotal (compat. con vistas existentes)
        ViewBag.Shipping = totals.Shipping;      // envío cotizado
        ViewBag.GrandTotal = totals.GrandTotal;

        ViewBag.ShippingStatus = totals.Status.ToString();
        ViewBag.ShippingMessage = totals.Message;
        ViewBag.ShippingCarrier = totals.CarrierName;
        ViewBag.ShippingCarrierLogo = totals.CarrierLogoUrl;
        ViewBag.ShippingDays = totals.EstimatedDays;

        return totals;
    }

    /// <summary>
    /// Todo lo que necesita la vista de checkout: totales + catálogo de departamentos y
    /// municipios del departamento elegido (para poder re-renderizar el formulario
    /// completo si el POST falla) + el estado del cotizador.
    /// </summary>
    private async Task<CartTotals> PrepareCheckoutViewAsync(
        IEnumerable<ShoppingCartItem> cartItems,
        string? destinationDaneCode,
        string? department,
        bool domestic = true,
        CancellationToken ct = default)
    {
        var totals = await SetCartTotalsAsync(cartItems, destinationDaneCode, domestic, ct);

        // Fuera de Colombia no hay selector de ciudad: el catálogo DIVIPOLA no aplica.
        ViewBag.IsDomestic = domestic;
        if (!domestic)
        {
            ViewBag.Departments = Array.Empty<string>();
            ViewBag.Cities = Array.Empty<ShippingCityItem>();
            ViewBag.SelectedDepartment = string.IsNullOrWhiteSpace(department) ? null : department.Trim();
            ViewBag.SelectedCityDaneCode = null;
            ViewBag.SelectedCityName = null;
            ViewBag.ShippingRequiresCity = false;
            return totals;
        }

        var selectedCity = await _shippingCityService.FindByDaneCodeAsync(destinationDaneCode, ct);

        // Si el municipio elegido manda, su departamento gana sobre el posteado.
        var selectedDepartment = selectedCity?.Department
            ?? (string.IsNullOrWhiteSpace(department) ? null : department.Trim());

        ViewBag.Departments = await _shippingCityService.GetDepartmentsAsync(ct);
        ViewBag.SelectedDepartment = selectedDepartment;
        ViewBag.Cities = string.IsNullOrWhiteSpace(selectedDepartment)
            ? Array.Empty<ShippingCityItem>()
            : await _shippingCityService.GetCitiesAsync(selectedDepartment, ct);

        ViewBag.SelectedCityDaneCode = selectedCity?.DaneCode;
        ViewBag.SelectedCityName = selectedCity?.Name;

        // Todo pedido nacional exige ciudad: el POST la valida contra el catálogo y sin
        // ella no hay orden. La UI refleja esa regla (botón bloqueado hasta elegirla),
        // independientemente del proveedor de cotización configurado.
        ViewBag.ShippingRequiresCity = true;

        return totals;
    }

    private async Task<List<ShoppingCartItem>> GetCartItemsAsync()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var user = await _userManager.GetUserAsync(User);
            return await _context.ShoppingCartItems
                .Include(c => c.Product)
                .ThenInclude(p => p.Provider)
                .Where(c => c.UserID == user.Id)
                .ToListAsync();
        }
        else
        {
            var sessionCart = GetSessionCart();
            // Re-hydrate Products from DB because Session only stores IDs/Basic info safely
            var productIds = sessionCart.Select(c => c.ProductID).ToList();
            var products = await _context.Products
                .Include(p => p.Provider)
                .Where(p => productIds.Contains(p.ProductID))
                .ToListAsync();

            foreach (var item in sessionCart)
            {
                item.Product = products.FirstOrDefault(p => p.ProductID == item.ProductID);
            }
            return sessionCart;
        }
    }

    private List<ShoppingCartItem> GetSessionCart()
    {
        var sessionJson = HttpContext.Session.GetString("ShoppingCart");
        if (string.IsNullOrEmpty(sessionJson)) return new List<ShoppingCartItem>();

        // Custom deserializer options if needed
        return JsonSerializer.Deserialize<List<ShoppingCartItem>>(sessionJson) ?? new List<ShoppingCartItem>();
    }

    private void SaveSessionCart(List<ShoppingCartItem> cart)
    {
        // Avoid circular reference of Product navigation property when saving to session
        // Create a DTO or just set Product to null temporally?
        // Better: create a lightweight list to save.
        
        var listToSave = cart.Select(c => new ShoppingCartItem 
        { 
            ShoppingCartItemID = c.ShoppingCartItemID,
            ProductID = c.ProductID,
            Quantity = c.Quantity,
            CreatedOn = c.CreatedOn,
            // UserID is null
            // Product is null (don't save)
        }).ToList();

        var json = JsonSerializer.Serialize(listToSave);
        HttpContext.Session.SetString("ShoppingCart", json);
    }
}

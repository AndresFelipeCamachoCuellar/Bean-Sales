using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Web.Data;
using Web.Models;
using Web.Models.Enums;
using Web.Services.Shipping;
using System.Text.Json;

namespace Web.Controllers;

public class CartController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IShippingQuoteService _shippingQuoteService;
    private readonly ShippingOptions _shippingOptions;

    public CartController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IShippingQuoteService shippingQuoteService,
        IOptions<ShippingOptions> shippingOptions)
    {
        _context = context;
        _userManager = userManager;
        _shippingQuoteService = shippingQuoteService;
        _shippingOptions = shippingOptions.Value;
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
    public async Task<IActionResult> Checkout()
    {
        // 1. Validate Auth
        if (User.Identity?.IsAuthenticated != true)
        {
            // Redirect to Register (Client requirement)
            return RedirectToAction("Register", "Account", new { returnUrl = Url.Action("Checkout", "Cart") });
        }

        // 2. Validate Cart Content
        var cartItems = await GetCartItemsAsync();
        if (!cartItems.Any())
        {
            return RedirectToAction(nameof(Index));
        }

        // 3. Show Checkout View (Summary, Address, Payment placeholder)
        await SetCartTotalsAsync(cartItems);
        return View(cartItems);
    }

    [HttpPost]
    public async Task<IActionResult> Checkout(string firstName, string lastName, string address,
        string country, string state, string zip, string paymentMethod)
    {
        // 1. Validate Auth (mismo criterio que el GET)
        if (User.Identity?.IsAuthenticated != true)
        {
            return RedirectToAction("Register", "Account", new { returnUrl = Url.Action("Checkout", "Cart") });
        }

        var user = await _userManager.GetUserAsync(User);

        // 2. Obtener carrito
        var cartItems = await GetCartItemsAsync();
        if (!cartItems.Any())
        {
            return RedirectToAction(nameof(Index));
        }

        // 3. Validar stock por ítem
        var sinStock = cartItems
            .FirstOrDefault(i => i.Product == null || i.Product.Stock < i.Quantity);
        if (sinStock != null)
        {
            var nombre = sinStock.Product?.Name ?? "un producto";
            TempData["CheckoutError"] = $"No hay stock suficiente para \"{nombre}\". Ajusta la cantidad e inténtalo de nuevo.";
            await SetCartTotalsAsync(cartItems);
            return View(cartItems);
        }

        // 3b. Cotizar el envío una sola vez para esta compra (misma tarifa que ve el cliente).
        var totals = await SetCartTotalsAsync(cartItems);

        // 4-7. Crear orden + descontar stock + vaciar carrito de forma atómica
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var order = new Order
            {
                OrderID = Guid.NewGuid(),
                UserID = user.Id,
                OrderDate = DateTime.Now,
                OrderStatus = OrderStatus.Confirmed,
                FirstName = firstName,
                LastName = lastName,
                Address = address,
                State = state,
                ZipCode = zip,
                ShippingCountry = country,
                PaymentMethod = paymentMethod,
                CurrencyCode = "COP",
                // Snapshot de la cotización de envío usada al cobrar.
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

            order.Subtotal = subtotal;
            order.ShippingCost = totals.Shipping; // cotizado por IShippingQuoteService
            order.TotalAmount = order.Subtotal + order.ShippingCost;

            _context.Orders.Add(order);

            // 6. Vaciar carrito del usuario
            var userCart = await _context.ShoppingCartItems
                .Where(c => c.UserID == user.Id)
                .ToListAsync();
            _context.ShoppingCartItems.RemoveRange(userCart);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // 8. Confirmación
            return RedirectToAction(nameof(Confirmation), new { id = order.OrderID });
        }
        catch
        {
            await transaction.RollbackAsync();
            TempData["CheckoutError"] = "No se pudo procesar tu pedido. Inténtalo de nuevo.";
            await SetCartTotalsAsync(cartItems);
            return View(cartItems);
        }
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
        string Source);

    /// <summary>
    /// Fuente ÚNICA de los totales del carrito: cotiza el envío y publica
    /// ViewBag.Total (subtotal), ViewBag.Shipping y ViewBag.GrandTotal.
    /// Todas las rutas que renderizan Cart/Index o Cart/Checkout pasan por aquí.
    /// </summary>
    private async Task<CartTotals> SetCartTotalsAsync(
        IEnumerable<ShoppingCartItem> cartItems,
        string? destinationDaneCode = null,
        CancellationToken ct = default)
    {
        var items = cartItems as IList<ShoppingCartItem> ?? cartItems.ToList();

        var subtotal = items.Sum(i => i.Quantity * (i.Product?.Price ?? 0m));

        // Un solo bulto agregado con la heurística compartida (peso/dimensiones/valor declarado).
        var package = ShippingPackageBuilder.Build(
            items,
            _shippingOptions.Origin.DaneCode,
            destinationDaneCode ?? string.Empty,
            _shippingOptions.Defaults);

        var quote = await _shippingQuoteService.QuoteAsync(package.Request, ct);
        var cheapest = quote.Cheapest;

        // Sin opción cotizada se cae a la tarifa de configuración: el checkout nunca se queda sin precio.
        var shipping = cheapest?.Price ?? _shippingOptions.FallbackCost;

        ViewBag.Total = subtotal;               // subtotal (compat. con vistas existentes)
        ViewBag.Shipping = shipping;            // envío cotizado
        ViewBag.GrandTotal = subtotal + shipping;

        return new CartTotals(
            subtotal,
            shipping,
            subtotal + shipping,
            cheapest?.CarrierName,
            cheapest?.EstimatedDays,
            quote.Source);
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

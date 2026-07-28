using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Web.Data;
using Web.Models;

namespace Web.Controllers;

// Historial de pedidos del cliente (handoff vista 6: "Mis pedidos").
// El detalle/seguimiento de cada pedido se reutiliza desde Cart/Confirmation/{id}.
[Authorize]
public class OrdersController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public OrdersController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    // GET /Orders  → lista de pedidos del usuario, del más reciente al más antiguo.
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        // Aunque [Authorize] ya protege la acción, replicamos el patrón defensivo
        // del CartController para redirigir de forma amable si no hay sesión.
        if (User.Identity?.IsAuthenticated != true)
        {
            return RedirectToAction("Login", "Account", new { returnUrl = Url.Action("Index", "Orders") });
        }

        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return RedirectToAction("Login", "Account", new { returnUrl = Url.Action("Index", "Orders") });
        }

        ViewBag.FirstName = string.IsNullOrWhiteSpace(user.FirstName) ? user.UserName : user.FirstName;

        var orders = await _context.Orders
            .Where(o => o.UserID == user.Id)
            .Include(o => o.Items)
            .ThenInclude(i => i.Product)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync();

        return View(orders);
    }
}

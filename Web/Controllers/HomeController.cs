using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using Web.Data;
using Web.Models;
using Web.Models.Enums;
using Web.Models.ViewModels;

namespace Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HomeController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
             // 1. Get User Country (Cookie or Default)
            var selectedCountryIdStr = HttpContext.Request.Cookies["UserCountryId"];
            Guid? selectedCountryId = null;

            if (Guid.TryParse(selectedCountryIdStr, out var parsedId))
            {
                selectedCountryId = parsedId;
            }
            else
            {
                // Default to Colombia or first available
                var defaultCountry = await _context.Countries.FirstOrDefaultAsync(c => c.Name == "Colombia" && c.Status);
                if (defaultCountry != null)
                {
                    selectedCountryId = defaultCountry.CountryID;
                    // Set cookie for consistency
                     Response.Cookies.Append("UserCountryId", selectedCountryId.Value.ToString(), new CookieOptions { Expires = DateTime.Now.AddDays(30) });
                }
            }
            
            ViewBag.CurrentCountryId = selectedCountryId;

            // 2. Fetch Active Products Available in this Country
            List<Product> products = new List<Product>();

            if (selectedCountryId.HasValue)
            {
                products = await _context.Products
                    .Include(p => p.Provider)
                    .Include(p => p.ProductCountries)
                    .Where(p => 
                        p.Status && 
                        p.ProductStatus == ProductStatus.Active &&
                        p.ProductCountries.Any(pc => pc.CountryID == selectedCountryId.Value && pc.IsAvailable)
                    )
                    .OrderByDescending(p => p.CreatedOn)
                    .ToListAsync();
            }

            return View(products);
        }

        // GET: /Home/Origenes
        // Página pública de orígenes. Usa EXACTAMENTE el mismo criterio de
        // visibilidad que Index() (país seleccionado por cookie + Status +
        // ProductStatus.Active + ProductCountry.IsAvailable) y agrupa por región
        // EN MEMORIA (tras ToListAsync) para no traducir GroupBy a SQL.
        public async Task<IActionResult> Origenes()
        {
            // 1. País del usuario (cookie o Colombia por defecto) — igual que Index().
            var selectedCountryIdStr = HttpContext.Request.Cookies["UserCountryId"];
            Guid? selectedCountryId = null;

            if (Guid.TryParse(selectedCountryIdStr, out var parsedId))
            {
                selectedCountryId = parsedId;
            }
            else
            {
                var defaultCountry = await _context.Countries.FirstOrDefaultAsync(c => c.Name == "Colombia" && c.Status);
                if (defaultCountry != null)
                {
                    selectedCountryId = defaultCountry.CountryID;
                    Response.Cookies.Append("UserCountryId", selectedCountryId.Value.ToString(), new CookieOptions { Expires = DateTime.Now.AddDays(30) });
                }
            }

            ViewBag.CurrentCountryId = selectedCountryId;

            // 2. Mismos productos que ve el catálogo.
            List<Product> products = new List<Product>();

            if (selectedCountryId.HasValue)
            {
                products = await _context.Products
                    .Include(p => p.Provider)
                    .Include(p => p.ProductCountries)
                    .Where(p =>
                        p.Status &&
                        p.ProductStatus == ProductStatus.Active &&
                        p.ProductCountries.Any(pc => pc.CountryID == selectedCountryId.Value && pc.IsAvailable)
                    )
                    .OrderByDescending(p => p.CreatedOn)
                    .ToListAsync();
            }

            // 3. Agrupación en memoria por región (Origin null/vacío -> "Otros orígenes").
            var model = new OrigenesViewModel
            {
                Regions = products
                    .GroupBy(p => string.IsNullOrWhiteSpace(p.Origin) ? "Otros orígenes" : p.Origin!.Trim())
                    .Select(g => new OriginRegionViewModel
                    {
                        Name = g.Key,
                        LotCount = g.Count(),
                        Farms = g.Where(p => !string.IsNullOrWhiteSpace(p.Farm))
                                 .Select(p => p.Farm!.Trim())
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .OrderBy(f => f)
                                 .ToList(),
                        AltitudeRange = BuildAltitudeRange(g.Select(p => p.Altitude)),
                        Products = g.ToList()
                    })
                    .OrderByDescending(r => r.LotCount)
                    .ThenBy(r => r.Name)
                    .ToList()
            };

            return View(model);
        }

        // GET: /Home/Nosotros — página de marca, contenido estático (sin datos).
        public IActionResult Nosotros()
        {
            return View();
        }

        // Construye un rango de altura legible a partir de los Altitude (string libre,
        // ej. "1.750 msnm"). Extrae los dígitos; ignora los que no se puedan interpretar.
        private static string? BuildAltitudeRange(IEnumerable<string?> altitudes)
        {
            var values = new List<int>();

            foreach (var raw in altitudes)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                var digits = new string(raw.Where(char.IsDigit).ToArray());
                if (digits.Length == 0)
                {
                    continue;
                }

                if (int.TryParse(digits, out var meters) && meters > 0 && meters < 10000)
                {
                    values.Add(meters);
                }
            }

            if (values.Count == 0)
            {
                return null;
            }

            var co = new System.Globalization.CultureInfo("es-CO");
            var min = values.Min();
            var max = values.Max();

            return min == max
                ? $"{min.ToString("#,##0", co)} msnm"
                : $"{min.ToString("#,##0", co)} – {max.ToString("#,##0", co)} msnm";
        }

        public async Task<IActionResult> Details(Guid id)
        {
            var product = await _context.Products
                .Include(p => p.Provider)
                .Include(p => p.ProductCountries)
                .ThenInclude(pc => pc.Country)
                // Include FILTRADO: la galería solo se carga AQUÍ. El catálogo (Index),
                // Origenes, el carrito y los pedidos usan Product.ImageUrl (la portada
                // denormalizada) para no traer N imágenes por producto.
                .Include(p => p.Images.Where(i => i.Status))
                .FirstOrDefaultAsync(p => p.ProductID == id && p.Status); // Ensure active? Status=true means not soft deleted.
                // Should we check ProductStatus.Active? Ideally yes, but maybe user wants to share link?
                // Let's hide if not active for public.
            
            if (product == null || product.ProductStatus != ProductStatus.Active)
            {
                return NotFound();
            }

            return View(product);
        }

        [HttpPost]
        public IActionResult SetCountry(Guid countryId)
        {
            Response.Cookies.Append("UserCountryId", countryId.ToString(), new CookieOptions { Expires = DateTime.Now.AddDays(30) });
            return RedirectToAction(nameof(Index));
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using Web.Data;
using Web.Models;
using Web.Models.Enums;

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

        public async Task<IActionResult> Details(Guid id)
        {
            var product = await _context.Products
                .Include(p => p.Provider)
                .Include(p => p.ProductCountries)
                .ThenInclude(pc => pc.Country)
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

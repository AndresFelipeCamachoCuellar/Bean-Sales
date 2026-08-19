using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Web.Data;
using Web.Models;

namespace Web.ViewComponents;

public class CountrySelectorViewComponent : ViewComponent
{
    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _cache;
    private const string ActiveCountriesCacheKey = "CountrySelector.ActiveCountries.v1";

    public CountrySelectorViewComponent(ApplicationDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<IViewComponentResult> InvokeAsync(string variant = "Home")
    {
        var countries = await _cache.GetOrCreateAsync(ActiveCountriesCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await _context.Countries
                .AsNoTracking()
                .Where(c => c.Status)
                .OrderBy(c => c.Name)
                .ToListAsync();
        }) ?? new List<Country>();

        // Get selected country from Session or Cookie
        // For simplicity, we'll maintain it in a Cookie "UserCountryId"
        var selectedCountryIdStr = HttpContext.Request.Cookies["UserCountryId"];
        Country? selectedCountry = null;

        if (Guid.TryParse(selectedCountryIdStr, out var parsedId))
        {
            selectedCountry = countries.FirstOrDefault(country => country.CountryID == parsedId);
        }

        if (selectedCountry == null)
        {
            selectedCountry = countries.FirstOrDefault(c => c.Name == "Colombia")
                ?? countries.FirstOrDefault();

            if (selectedCountry != null)
            {
                HttpContext.Response.Cookies.Append(
                    "UserCountryId",
                    selectedCountry.CountryID.ToString(),
                    new CookieOptions { Expires = DateTime.Now.AddDays(30) });
            }
        }

        ViewBag.SelectedCountryId = selectedCountry?.CountryID;
        return View("Home", countries);
    }
}

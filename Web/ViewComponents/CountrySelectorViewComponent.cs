using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Web.Data;
using Web.Models;

namespace Web.ViewComponents;

public class CountrySelectorViewComponent : ViewComponent
{
    private readonly ApplicationDbContext _context;

    public CountrySelectorViewComponent(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var countries = await _context.Countries
            .Where(c => c.Status)
            .OrderBy(c => c.Name)
            .ToListAsync();

        // Get selected country from Session or Cookie
        // For simplicity, we'll maintain it in a Cookie "UserCountryId"
        var selectedCountryIdStr = HttpContext.Request.Cookies["UserCountryId"];
        Guid? selectedCountryId = null;

        if (Guid.TryParse(selectedCountryIdStr, out var parsedId))
        {
            selectedCountryId = parsedId;
        }
        else
        {
            // Default to Colombia to match HomeController logic
            var defaultCountry = countries.FirstOrDefault(c => c.Name == "Colombia");
            if (defaultCountry == null)
            {
                defaultCountry = countries.FirstOrDefault();
            }

            if (defaultCountry != null)
            {
                selectedCountryId = defaultCountry.CountryID;
            }
        }

        ViewBag.SelectedCountryId = selectedCountryId;
        return View(countries);
    }
}

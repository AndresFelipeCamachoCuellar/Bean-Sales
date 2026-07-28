using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Web.Data;

namespace Web.Services.Shipping;

/// <summary>
/// Catálogo de municipios con caché en memoria. Se cachea UNA sola lista completa
/// (~170 filas hoy, ~1.100 con el catálogo completo) y el filtro por departamento
/// se resuelve en memoria: más simple y con mejor hit rate que una entrada por departamento.
/// </summary>
public sealed class ShippingCityService : IShippingCityService
{
    private const string CitiesCacheKey = "shipping:cities:all";
    private const string DepartmentsCacheKey = "shipping:departments";

    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ShippingOptions _options;

    public ShippingCityService(
        ApplicationDbContext context,
        IMemoryCache cache,
        IOptions<ShippingOptions> options)
    {
        _context = context;
        _cache = cache;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<string>> GetDepartmentsAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(DepartmentsCacheKey, out IReadOnlyList<string>? cached) && cached != null)
        {
            return cached;
        }

        var cities = await GetCitiesAsync(null, ct);
        var departments = cities
            .Select(c => c.Department)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _cache.Set(DepartmentsCacheKey, (IReadOnlyList<string>)departments, CacheDuration);
        return departments;
    }

    public async Task<IReadOnlyList<ShippingCityItem>> GetCitiesAsync(string? department = null, CancellationToken ct = default)
    {
        var all = await GetAllCitiesAsync(ct);

        if (string.IsNullOrWhiteSpace(department)) return all;

        var filtered = all
            .Where(c => string.Equals(c.Department, department.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        return filtered;
    }

    public async Task<ShippingCityItem?> FindByDaneCodeAsync(string? daneCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(daneCode)) return null;

        var code = daneCode.Trim();
        var all = await GetAllCitiesAsync(ct);

        return all.FirstOrDefault(c => string.Equals(c.DaneCode, code, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<ShippingCityItem>> GetAllCitiesAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(CitiesCacheKey, out IReadOnlyList<ShippingCityItem>? cached) && cached != null)
        {
            return cached;
        }

        // Proyección anónima + mapeo en memoria: evita depender de la traducción de
        // constructores de record dentro de la consulta EF.
        var rows = await _context.ShippingCities
            .AsNoTracking()
            .Where(c => c.Status)
            .OrderBy(c => c.Department)
            .ThenBy(c => c.Name)
            .Select(c => new { c.DaneCode, c.Name, c.Department })
            .ToListAsync(ct);

        var cities = rows
            .Select(r => new ShippingCityItem(r.DaneCode, r.Name, r.Department))
            .ToList();

        _cache.Set(CitiesCacheKey, (IReadOnlyList<ShippingCityItem>)cities, CacheDuration);
        return cities;
    }

    private TimeSpan CacheDuration =>
        TimeSpan.FromHours(_options.CityCacheHours > 0 ? _options.CityCacheHours : 12);
}

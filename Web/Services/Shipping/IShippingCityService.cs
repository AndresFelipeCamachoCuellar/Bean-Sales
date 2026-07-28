namespace Web.Services.Shipping;

/// <summary>Proyección ligera del catálogo de municipios, apta para cachear y para poblar selects.</summary>
public sealed record ShippingCityItem(string DaneCode, string Name, string Department);

/// <summary>
/// Acceso al catálogo de municipios (DIVIPOLA) con caché en memoria.
/// El catálogo vive en BD, así que el checkout renderiza aunque la API externa esté caída.
/// </summary>
public interface IShippingCityService
{
    /// <summary>Departamentos con al menos un municipio activo, ordenados alfabéticamente.</summary>
    Task<IReadOnlyList<string>> GetDepartmentsAsync(CancellationToken ct = default);

    /// <summary>Municipios activos; si se pasa departamento, filtra por él.</summary>
    Task<IReadOnlyList<ShippingCityItem>> GetCitiesAsync(string? department = null, CancellationToken ct = default);

    /// <summary>Valida server-side que un código DANE posteado exista de verdad.</summary>
    Task<ShippingCityItem?> FindByDaneCodeAsync(string? daneCode, CancellationToken ct = default);
}

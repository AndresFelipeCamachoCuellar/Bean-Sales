using Microsoft.AspNetCore.Identity;
using Web.Models;

namespace Web.Services.Tenancy;

/// <summary>
/// Servicio inyectable que resuelve el <see cref="ProviderScopeSnapshot"/> del usuario de la
/// petición actual. Se registra como <c>Scoped</c>, así que la consulta a Identity se hace
/// UNA vez por request aunque el controlador pida el ámbito varias veces.
/// </summary>
public interface IProviderScope
{
    /// <summary>Ámbito multi-tenant del usuario actual (cacheado durante la petición).</summary>
    Task<ProviderScopeSnapshot> CurrentAsync();

    /// <summary>
    /// Igual que <see cref="CurrentAsync"/> pero exige que el usuario pertenezca a una
    /// empresa proveedora. Devuelve <c>null</c> si es staff de Bean o anónimo, para que el
    /// controlador responda <c>Forbid()</c> — es el reemplazo literal del
    /// <c>if (currentUser?.ProviderID == null) return Forbid();</c> repetido a mano.
    /// </summary>
    Task<ProviderScopeSnapshot?> RequireProviderAsync();
}

/// <inheritdoc cref="IProviderScope"/>
public sealed class ProviderScope : IProviderScope
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    private ProviderScopeSnapshot? _cached;

    public ProviderScope(IHttpContextAccessor httpContextAccessor, UserManager<ApplicationUser> userManager)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public async Task<ProviderScopeSnapshot> CurrentAsync()
    {
        if (_cached is not null) return _cached;

        var principal = _httpContextAccessor.HttpContext?.User;

        if (principal?.Identity?.IsAuthenticated != true)
        {
            return _cached = ProviderScopeSnapshot.Anonymous;
        }

        // ⚠️ El ProviderID se lee de la BD, NO de un claim. Un claim se emite al iniciar
        // sesión y sobreviviría a que un admin mueva o desvincule al usuario de su empresa:
        // la sesión vieja seguiría viendo los datos del proveedor anterior hasta el próximo
        // login. La consulta va a la tabla de usuarios por clave primaria y el resultado se
        // cachea por request, así que el costo es una lectura indexada por petición.
        var user = await _userManager.GetUserAsync(principal);

        if (user is null)
        {
            return _cached = ProviderScopeSnapshot.Anonymous;
        }

        return _cached = user.ProviderID.HasValue
            ? ProviderScopeSnapshot.ForProvider(user.Id, user.ProviderID.Value)
            : ProviderScopeSnapshot.ForStaff(user.Id);
    }

    public async Task<ProviderScopeSnapshot?> RequireProviderAsync()
    {
        var scope = await CurrentAsync();
        return scope.IsProvider ? scope : null;
    }
}

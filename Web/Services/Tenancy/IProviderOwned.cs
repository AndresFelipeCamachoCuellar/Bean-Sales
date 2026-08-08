namespace Web.Services.Tenancy;

/// <summary>
/// Entidad que SIEMPRE pertenece a un proveedor (hoy: <see cref="Models.Product"/>).
///
/// Es un marcador de compilación: <see cref="ProviderScopeSnapshot.ApplyTo{T}"/> solo acepta
/// tipos que lo implementen, así que "olvidar el filtro" deja de ser posible por accidente
/// — o el tipo está marcado y el filtro se aplica, o el código ni siquiera compila.
///
/// ⚠️ La propiedad DEBE llamarse <c>ProviderID</c> y estar mapeada por EF: el filtro se
/// construye por reflexión sobre el tipo CONCRETO (no sobre la interfaz) para que EF Core
/// pueda traducirlo a SQL.
/// </summary>
public interface IProviderOwned
{
    Guid ProviderID { get; }
}

/// <summary>
/// Entidad que PUEDE pertenecer a un proveedor o directamente a Bean
/// (<see cref="Models.ApplicationUser"/>, <see cref="Models.ApplicationRole"/>:
/// <c>ProviderID == null</c> significa "es de Bean, no de ninguna empresa proveedora").
///
/// Se separa de <see cref="IProviderOwned"/> a propósito: el tipo de la columna cambia
/// (<c>Guid</c> vs <c>Guid?</c>) y mezclarlos obligaría a construir la comparación a ciegas.
/// </summary>
public interface IOptionallyProviderOwned
{
    Guid? ProviderID { get; }
}

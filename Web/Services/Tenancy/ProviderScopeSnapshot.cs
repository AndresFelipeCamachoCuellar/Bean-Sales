using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Web.Services.Tenancy;

/// <summary>
/// Ámbito multi-tenant YA RESUELTO del usuario de la petición: quién es y a qué proveedor
/// pertenece. Es un objeto de valor inmutable y sin dependencias de ASP.NET, así que se
/// puede construir a mano en los tests (<see cref="ForProvider"/>, <see cref="ForStaff"/>)
/// sin levantar un <c>HttpContext</c>.
///
/// ─────────────────────────────────────────────────────────────────────────────────────
/// POR QUÉ EXISTE
/// ─────────────────────────────────────────────────────────────────────────────────────
/// Hasta ahora el aislamiento entre proveedores era una CONVENCIÓN copiada a mano en cada
/// controlador (<c>.Where(p =&gt; p.ProviderID == currentUser.ProviderID)</c>). Una
/// convención se olvida: basta un <c>Details/{id}</c> nuevo que cargue por ID sin el filtro
/// para exponer costos, márgenes o pedidos de otra empresa (IDOR).
///
/// Aquí se convierte en MECANISMO: <see cref="ApplyTo{T}"/> solo acepta tipos marcados con
/// <see cref="IProviderOwned"/>, y <see cref="SingleOwnedAsync{T}"/> hace imposible cargar
/// por ID sin comprobar la pertenencia — el filtro no es opcional, lo pone el helper.
///
/// ─────────────────────────────────────────────────────────────────────────────────────
/// REGLA DE NEGOCIO: QUÉ HACE EL FILTRO CON EL STAFF DE BEAN
/// ─────────────────────────────────────────────────────────────────────────────────────
/// Un usuario SIN <c>ProviderID</c> es staff de Bean y su back-office es GLOBAL por diseño
/// (aprobaciones, precios, inventario y pedidos son de todo el catálogo). Por eso
/// <see cref="ApplyTo{T}"/> devuelve la consulta SIN TOCAR para el staff.
///
/// ⚠️ Consecuencia: en una pantalla que debe ser exclusiva del proveedor hay que exigir
/// primero que el usuario TENGA proveedor (<see cref="IsProvider"/>) y devolver
/// <c>Forbid()</c> si no — igual que hacían los controladores antes. Este objeto decide
/// "qué filas", no "quién entra"; eso último sigue siendo de <c>[HasPermission]</c>.
/// </summary>
public sealed class ProviderScopeSnapshot
{
    /// <summary>Usuario de Identity. <c>null</c> = petición anónima.</summary>
    public Guid? CurrentUserId { get; }

    /// <summary>Empresa proveedora del usuario. <c>null</c> = staff de Bean (o anónimo).</summary>
    public Guid? CurrentProviderId { get; }

    private ProviderScopeSnapshot(Guid? userId, Guid? providerId)
    {
        CurrentUserId = userId;
        CurrentProviderId = providerId;
    }

    /// <summary>Petición sin sesión.</summary>
    public static readonly ProviderScopeSnapshot Anonymous = new(null, null);

    /// <summary>Usuario que pertenece a una empresa proveedora.</summary>
    public static ProviderScopeSnapshot ForProvider(Guid userId, Guid providerId) =>
        new(userId, providerId);

    /// <summary>Usuario interno de Bean (sin <c>ProviderID</c>).</summary>
    public static ProviderScopeSnapshot ForStaff(Guid userId) => new(userId, null);

    public bool IsAuthenticated => CurrentUserId.HasValue;

    /// <summary>El usuario pertenece a una empresa proveedora: solo puede ver lo suyo.</summary>
    public bool IsProvider => CurrentProviderId.HasValue;

    /// <summary>Usuario interno de Bean: alcance global.</summary>
    public bool IsStaff => IsAuthenticated && !CurrentProviderId.HasValue;

    // ================================================================== Pertenencia

    /// <summary>
    /// ¿El ámbito actual puede tocar una entidad de este proveedor? Para el staff de Bean
    /// siempre es <c>true</c>; para un proveedor, solo si es el suyo. Sirve para entidades
    /// YA cargadas (por ejemplo, después de un <c>Include</c>).
    /// </summary>
    public bool Owns(Guid providerId) =>
        !IsProvider || CurrentProviderId!.Value == providerId;

    /// <inheritdoc cref="Owns(Guid)"/>
    /// <remarks>
    /// Un <c>ProviderID</c> nulo significa "de Bean". Un proveedor NUNCA es dueño de una
    /// entidad de Bean, así que devuelve <c>false</c>; el staff sí.
    /// </remarks>
    public bool Owns(Guid? providerId)
    {
        if (!IsProvider) return true;
        return providerId.HasValue && providerId.Value == CurrentProviderId!.Value;
    }

    // ==================================================================== Consultas

    /// <summary>
    /// Recorta la consulta a lo que puede ver el ámbito actual. Para el staff de Bean la
    /// devuelve intacta (ver la nota de arriba).
    /// </summary>
    public IQueryable<T> ApplyTo<T>(IQueryable<T> query) where T : class, IProviderOwned
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!IsProvider) return query;

        return query.Where(BuildProviderPredicate<T>(CurrentProviderId!.Value, nullableColumn: false));
    }

    /// <summary>
    /// Igual que <see cref="ApplyTo{T}"/> para entidades que pueden ser de un proveedor o de
    /// Bean (usuarios y roles). Un proveedor solo ve las suyas: las de Bean
    /// (<c>ProviderID == null</c>) quedan fuera.
    /// </summary>
    public IQueryable<T> ApplyToShared<T>(IQueryable<T> query) where T : class, IOptionallyProviderOwned
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!IsProvider) return query;

        return query.Where(BuildProviderPredicate<T>(CurrentProviderId!.Value, nullableColumn: true));
    }

    // =============================================== Cargar por ID + pertenencia

    /// <summary>
    /// EL helper que cierra el IDOR: carga UNA entidad por su id y, en la MISMA consulta,
    /// comprueba que sea del proveedor del usuario. Si es de otro, devuelve <c>null</c> y el
    /// controlador responde <c>NotFound()</c> — nunca se llega a materializar la fila ajena.
    ///
    /// El llamador solo aporta el predicado de la CLAVE; el de la PERTENENCIA lo pone este
    /// método, así que no se puede omitir.
    /// </summary>
    /// <example>
    /// <code>
    /// var product = await scope.SingleOwnedAsync(
    ///     _context.Products.Include(p =&gt; p.ProductCountries),
    ///     p =&gt; p.ProductID == id);
    /// if (product is null) return NotFound();
    /// </code>
    /// </example>
    public Task<T?> SingleOwnedAsync<T>(
        IQueryable<T> source,
        Expression<Func<T, bool>> byId,
        CancellationToken cancellationToken = default)
        where T : class, IProviderOwned
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(byId);

        return ApplyTo(source.Where(byId)).FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc cref="SingleOwnedAsync{T}(IQueryable{T}, Expression{Func{T, bool}}, CancellationToken)"/>
    public Task<T?> SingleOwnedSharedAsync<T>(
        IQueryable<T> source,
        Expression<Func<T, bool>> byId,
        CancellationToken cancellationToken = default)
        where T : class, IOptionallyProviderOwned
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(byId);

        return ApplyToShared(source.Where(byId)).FirstOrDefaultAsync(cancellationToken);
    }

    // ====================================================================== Interno

    /// <summary>
    /// Construye <c>e =&gt; e.ProviderID == valor</c> por reflexión sobre el tipo CONCRETO.
    ///
    /// Dos detalles que no son cosméticos:
    ///  · Se resuelve la propiedad de la CLASE, no la de la interfaz: EF Core traduce a SQL
    ///    los accesos a propiedades mapeadas, y una propiedad de interfaz no lo está.
    ///  · El valor viaja dentro de una caja (<see cref="ValueBox{TValue}"/>) en vez de un
    ///    <c>Expression.Constant</c> suelto. EF ve "acceso a miembro sobre una constante" —
    ///    exactamente lo que genera un closure del compilador — y lo convierte en un
    ///    PARÁMETRO de SQL. Con una constante incrustada, cada proveedor generaría un plan
    ///    distinto y reventaría la caché de consultas.
    /// </summary>
    private static Expression<Func<T, bool>> BuildProviderPredicate<T>(Guid providerId, bool nullableColumn)
    {
        var property = ProviderIdProperty(typeof(T));

        var parameter = Expression.Parameter(typeof(T), "e");
        var left = Expression.Property(parameter, property);

        var box = new ValueBox<Guid>(providerId);
        Expression right = Expression.Field(Expression.Constant(box), nameof(ValueBox<Guid>.Value));

        if (nullableColumn)
        {
            right = Expression.Convert(right, typeof(Guid?));
        }

        return Expression.Lambda<Func<T, bool>>(Expression.Equal(left, right), parameter);
    }

    private const string ProviderIdColumn = "ProviderID";

    private static readonly ConcurrentDictionary<Type, PropertyInfo> PropertyCache = new();

    private static PropertyInfo ProviderIdProperty(Type type) =>
        PropertyCache.GetOrAdd(type, static t =>
            t.GetProperty(ProviderIdColumn, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                $"El tipo {t.Name} está marcado como propiedad de un proveedor pero no expone " +
                $"una propiedad pública '{ProviderIdColumn}'. Sin ella el filtro multi-tenant " +
                "no se puede traducir a SQL."));

    /// <summary>Caja que hace que EF parametrice el valor en vez de incrustarlo.</summary>
    private sealed class ValueBox<TValue>
    {
        public readonly TValue Value;
        public ValueBox(TValue value) => Value = value;
    }
}

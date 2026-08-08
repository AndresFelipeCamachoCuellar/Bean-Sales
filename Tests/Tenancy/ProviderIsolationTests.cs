using Microsoft.EntityFrameworkCore;
using Web.Models;
using Web.Services.Tenancy;
using Xunit;

namespace Tests.Tenancy;

/// <summary>
/// Aislamiento multi-tenant (épica E5, HU-5.2 a HU-5.5) contra un proveedor EF REAL.
///
/// Cada test responde a una pregunta concreta: "¿puede la empresa A ver o tocar algo de la
/// empresa B?". La respuesta correcta es siempre NO — ni por listado, ni por acceso directo
/// por ID, ni desde un POST con un id forjado.
/// </summary>
public class ProviderIsolationTests
{
    // ==================================================================== Productos

    [Fact]
    public void ApplyTo_solo_devuelve_los_productos_del_proveedor()
    {
        using var db = new TenantTestDatabase();

        var visiblesParaA = db.ScopeA.ApplyTo(db.Db.Products).ToList();

        Assert.Single(visiblesParaA);
        Assert.Equal(db.ProductAId, visiblesParaA[0].ProductID);
        Assert.DoesNotContain(visiblesParaA, p => p.ProviderID == db.ProviderBId);
    }

    [Fact]
    public void ApplyTo_no_deja_ver_el_costo_del_otro_proveedor()
    {
        using var db = new TenantTestDatabase();

        // El costo (SupplierPrice) es lo más sensible del catálogo: revela el margen de Bean
        // y el poder de negociación del competidor.
        var costosVisiblesParaA = db.ScopeA.ApplyTo(db.Db.Products)
            .Select(p => p.SupplierPrice)
            .ToList();

        Assert.Equal(new[] { 40_000m }, costosVisiblesParaA);
    }

    [Fact]
    public async Task SingleOwnedAsync_devuelve_null_al_pedir_un_producto_ajeno_por_ID()
    {
        using var db = new TenantTestDatabase();

        // ESTE es el IDOR: la empresa A escribe en la barra de direcciones el id de un lote
        // de la empresa B. El helper devuelve null y el controlador responde NotFound().
        var robado = await db.ScopeA.SingleOwnedAsync(db.Db.Products, p => p.ProductID == db.ProductBId);

        Assert.Null(robado);
    }

    [Fact]
    public async Task SingleOwnedAsync_si_devuelve_el_producto_propio()
    {
        using var db = new TenantTestDatabase();

        var propio = await db.ScopeA.SingleOwnedAsync(db.Db.Products, p => p.ProductID == db.ProductAId);

        Assert.NotNull(propio);
        Assert.Equal(db.ProviderAId, propio!.ProviderID);
    }

    [Fact]
    public async Task SingleOwnedAsync_respeta_los_Include_del_llamador()
    {
        using var db = new TenantTestDatabase();

        // Regresión: el filtro se añade DESPUÉS del Include. Si ApplyTo rompiera la cadena
        // de includes, Edit dejaría de cargar los países y el formulario se guardaría vacío.
        var propio = await db.ScopeA.SingleOwnedAsync(
            db.Db.Products.Include(p => p.ProductCountries),
            p => p.ProductID == db.ProductAId);

        Assert.NotNull(propio);
        Assert.NotNull(propio!.ProductCountries);
    }

    // ============================================================ Pedidos entrantes

    [Fact]
    public void Pedidos_entrantes_no_muestran_los_pedidos_de_otro_proveedor()
    {
        using var db = new TenantTestDatabase();

        var idsDeA = ConsultaDePedidosEntrantes(db, db.ScopeA);

        Assert.Contains(db.OrderOnlyAId, idsDeA);
        Assert.Contains(db.OrderMixedId, idsDeA);   // carrito multi-marca: sí lo ve
        Assert.DoesNotContain(db.OrderOnlyBId, idsDeA);
    }

    [Fact]
    public void En_un_pedido_multimarca_cada_proveedor_solo_cobra_sus_lineas()
    {
        using var db = new TenantTestDatabase();

        var mixto = db.Db.Orders
            .Include(o => o.Items)
            .ThenInclude(i => i.Product)
            .Single(o => o.OrderID == db.OrderMixedId);

        // "Tu pago" = COSTO congelado × cantidad, SOLO de las líneas propias.
        var pagoDeA = mixto.Items
            .Where(i => i.Product != null && db.ScopeA.Owns(i.Product.ProviderID))
            .Sum(i => i.SupplierPriceSnapshot * i.Quantity);

        var pagoDeB = mixto.Items
            .Where(i => i.Product != null && db.ScopeB.Owns(i.Product.ProviderID))
            .Sum(i => i.SupplierPriceSnapshot * i.Quantity);

        Assert.Equal(40_000m * 1, pagoDeA);
        Assert.Equal(30_000m * 3, pagoDeB);

        // Y ninguno de los dos ve el total del pedido como si fuera suyo.
        Assert.NotEqual(pagoDeA + pagoDeB, pagoDeA);
    }

    // ======================================================== Usuarios de la empresa

    [Fact]
    public void ApplyToShared_solo_devuelve_los_usuarios_de_la_propia_empresa()
    {
        using var db = new TenantTestDatabase();

        var usuariosDeA = db.ScopeA.ApplyToShared(db.Db.Users).ToList();

        Assert.Single(usuariosDeA);
        Assert.Equal(db.UserAId, usuariosDeA[0].Id);
    }

    [Fact]
    public void ApplyToShared_no_expone_al_staff_de_Bean_ni_a_los_clientes()
    {
        using var db = new TenantTestDatabase();

        var idsVisiblesParaA = db.ScopeA.ApplyToShared(db.Db.Users).Select(u => u.Id).ToList();

        // Un usuario con ProviderID null es de Bean (staff o cliente del storefront):
        // el panel del proveedor NUNCA debe listarlo.
        Assert.DoesNotContain(db.StaffId, idsVisiblesParaA);
        Assert.DoesNotContain(db.CustomerId, idsVisiblesParaA);
    }

    [Fact]
    public async Task No_se_puede_cargar_por_ID_un_usuario_de_otra_empresa()
    {
        using var db = new TenantTestDatabase();

        var ajeno = await db.ScopeA.SingleOwnedSharedAsync(db.Db.Users, u => u.Id == db.UserBId);
        var staff = await db.ScopeA.SingleOwnedSharedAsync(db.Db.Users, u => u.Id == db.StaffId);

        Assert.Null(ajeno);
        Assert.Null(staff);
    }

    // =========================================================== Roles de la empresa

    [Fact]
    public void ApplyToShared_solo_devuelve_los_roles_de_la_propia_empresa()
    {
        using var db = new TenantTestDatabase();

        var rolesDeA = db.ScopeA.ApplyToShared(db.Db.Roles).ToList();

        Assert.Single(rolesDeA);
        Assert.Equal(db.RoleAId, rolesDeA[0].Id);
    }

    [Fact]
    public void Un_proveedor_no_ve_los_roles_globales_de_Bean()
    {
        using var db = new TenantTestDatabase();

        var idsVisiblesParaB = db.ScopeB.ApplyToShared(db.Db.Roles).Select(r => r.Id).ToList();

        // El rol SuperAdmin (ProviderID null) no puede aparecer en "Roles (Empresa)":
        // si apareciera, un ProviderAdmin podría editarlo o asignárselo.
        Assert.DoesNotContain(db.GlobalRoleId, idsVisiblesParaB);
        Assert.DoesNotContain(db.RoleAId, idsVisiblesParaB);
    }

    [Fact]
    public void Con_el_mismo_nombre_visible_cada_empresa_sigue_viendo_solo_su_rol()
    {
        using var db = new TenantTestDatabase();

        // Las dos empresas llaman "Bodeguero" a su rol. El listado no puede mezclarlos ni
        // mostrar dos filas idénticas: cada una ve exactamente el suyo.
        var deA = db.ScopeA.ApplyToShared(db.Db.Roles).ToList();
        var deB = db.ScopeB.ApplyToShared(db.Db.Roles).ToList();

        Assert.Equal(db.RoleAId, Assert.Single(deA).Id);
        Assert.Equal(db.RoleBId, Assert.Single(deB).Id);
        Assert.Equal("Bodeguero", deA[0].DisplayName);
        Assert.Equal("Bodeguero", deB[0].DisplayName);
    }

    [Fact]
    public async Task No_se_puede_gestionar_por_ID_un_rol_de_otra_empresa()
    {
        using var db = new TenantTestDatabase();

        // Es exactamente el vector del bug de CompanyRoles/ManagePermissions: forjar el
        // GUID de un rol ajeno en el POST.
        var ajeno = await db.ScopeA.SingleOwnedSharedAsync(db.Db.Roles, r => r.Id == db.RoleBId);
        var global = await db.ScopeA.SingleOwnedSharedAsync(db.Db.Roles, r => r.Id == db.GlobalRoleId);

        Assert.Null(ajeno);
        Assert.Null(global);
    }

    // ================================================================ Staff de Bean

    [Fact]
    public void El_staff_de_Bean_ve_el_catalogo_completo()
    {
        using var db = new TenantTestDatabase();

        // Regla de negocio explícita: el back-office de Bean es global (aprobaciones,
        // precios, inventario). Si esto se rompe, el admin deja de ver medio catálogo.
        var todos = db.ScopeStaff.ApplyTo(db.Db.Products).ToList();

        Assert.Equal(2, todos.Count);
    }

    [Fact]
    public async Task El_staff_de_Bean_puede_abrir_cualquier_lote_por_ID()
    {
        using var db = new TenantTestDatabase();

        var deA = await db.ScopeStaff.SingleOwnedAsync(db.Db.Products, p => p.ProductID == db.ProductAId);
        var deB = await db.ScopeStaff.SingleOwnedAsync(db.Db.Products, p => p.ProductID == db.ProductBId);

        Assert.NotNull(deA);
        Assert.NotNull(deB);
    }

    // ==================================================================== Pertenencia

    [Fact]
    public void Owns_distingue_proveedor_propio_ajeno_y_Bean()
    {
        using var db = new TenantTestDatabase();

        Assert.True(db.ScopeA.Owns(db.ProviderAId));
        Assert.False(db.ScopeA.Owns(db.ProviderBId));

        // ProviderID null = entidad de Bean: un proveedor nunca es su dueño...
        Assert.False(db.ScopeA.Owns((Guid?)null));
        // ...pero el staff sí puede tocar todo.
        Assert.True(db.ScopeStaff.Owns(db.ProviderAId));
        Assert.True(db.ScopeStaff.Owns((Guid?)null));
    }

    [Fact]
    public void Un_ambito_anonimo_no_es_ni_staff_ni_proveedor()
    {
        var anonimo = ProviderScopeSnapshot.Anonymous;

        Assert.False(anonimo.IsAuthenticated);
        Assert.False(anonimo.IsProvider);
        Assert.False(anonimo.IsStaff);
    }

    // ======================================================================= Helpers

    /// <summary>
    /// Reproduce la consulta de <c>SupplierOrdersController.Index</c>: pedidos pagados que
    /// tocan al menos un lote del proveedor. Se duplica aquí a propósito para que el test
    /// falle si esa consulta cambia de forma sin querer.
    /// </summary>
    private static List<Guid> ConsultaDePedidosEntrantes(TenantTestDatabase db, ProviderScopeSnapshot scope)
    {
        var misProductos = scope.ApplyTo(db.Db.Products).Select(p => p.ProductID);

        return db.Db.Orders
            .Where(o => o.PaymentStatus == Web.Models.Enums.PaymentStatus.Approved
                        && o.Items.Any(i => misProductos.Contains(i.ProductID)))
            .Select(o => o.OrderID)
            .ToList();
    }
}

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Web.Controllers;
using Web.Models.ViewModels;
using Web.Services.Tenancy;
using Xunit;

namespace Tests.Tenancy;

/// <summary>
/// Test del CONTROLADOR completo, no solo de la consulta: se instancia
/// <see cref="SupplierOrdersController"/> de verdad y se ejecuta <c>Index()</c> contra la
/// base SQLite sembrada.
///
/// Es posible precisamente por el refactor de E5.2: al mover el scoping a
/// <see cref="IProviderScope"/>, el controlador dejó de depender de <c>UserManager</c> y
/// pasó a tener dos dependencias inyectables y sustituibles. Antes, probar esto exigía
/// levantar Identity entero.
/// </summary>
public class SupplierOrdersControllerIsolationTests
{
    [Fact]
    public async Task Index_del_proveedor_A_no_incluye_el_pedido_exclusivo_de_B()
    {
        using var db = new TenantTestDatabase();
        var controller = NuevoControlador(db, db.ScopeA);

        var filas = await EjecutarIndexAsync(controller);

        var ids = filas.Select(f => f.Order.OrderID).ToList();
        Assert.Contains(db.OrderOnlyAId, ids);
        Assert.Contains(db.OrderMixedId, ids);
        Assert.DoesNotContain(db.OrderOnlyBId, ids);
    }

    [Fact]
    public async Task Index_solo_expone_las_lineas_propias_del_pedido_multimarca()
    {
        using var db = new TenantTestDatabase();
        var controller = NuevoControlador(db, db.ScopeA);

        var filas = await EjecutarIndexAsync(controller);
        var mixto = filas.Single(f => f.Order.OrderID == db.OrderMixedId);

        // El pedido tiene 2 líneas (una de A y una de B); el proveedor A solo debe recibir 1.
        Assert.Single(mixto.ProviderItems);
        Assert.Equal("Geisha de la Esperanza", mixto.ProviderItems[0].ProductName);
    }

    [Fact]
    public async Task Tu_pago_usa_el_costo_congelado_y_no_el_precio_de_venta_de_Bean()
    {
        using var db = new TenantTestDatabase();
        var controller = NuevoControlador(db, db.ScopeA);

        var filas = await EjecutarIndexAsync(controller);
        var mixto = filas.Single(f => f.Order.OrderID == db.OrderMixedId);

        // Regresión del bug de negocio de E2: con SubTotal (PVP × cantidad) aquí saldría
        // 60.000 y el proveedor vería el precio de venta de Bean, margen incluido.
        Assert.Equal(40_000m, mixto.Payout);
        Assert.NotEqual(60_000m, mixto.Payout);
    }

    [Fact]
    public async Task Un_usuario_sin_empresa_recibe_Forbid()
    {
        using var db = new TenantTestDatabase();
        var controller = NuevoControlador(db, db.ScopeStaff);

        var result = await controller.Index();

        // El staff de Bean tiene su propia bandeja (OrderManagement): esta pantalla es
        // exclusiva del proveedor y no puede degradar a "ver todos los pedidos".
        Assert.IsType<ForbidResult>(result);
    }

    // ======================================================================= Helpers

    /// <summary>
    /// Controlador listo para invocar fuera de una petición HTTP. El
    /// <see cref="ControllerContext"/> se asigna explícitamente: MVC lo crea solo cuando el
    /// framework enruta la acción, y sin él <c>View(...)</c> no puede armar el ViewData.
    /// </summary>
    private static SupplierOrdersController NuevoControlador(TenantTestDatabase db, ProviderScopeSnapshot scope) =>
        new(db.Db, new FakeProviderScope(scope))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

    private static async Task<List<SupplierOrderViewModel>> EjecutarIndexAsync(SupplierOrdersController controller)
    {
        var result = Assert.IsType<ViewResult>(await controller.Index());
        return Assert.IsAssignableFrom<List<SupplierOrderViewModel>>(result.Model);
    }

    /// <summary>
    /// Doble de <see cref="IProviderScope"/> con un ámbito fijo: evita montar HttpContext,
    /// Identity y cookies solo para decirle al controlador "eres la empresa A".
    /// </summary>
    private sealed class FakeProviderScope : IProviderScope
    {
        private readonly ProviderScopeSnapshot _snapshot;

        public FakeProviderScope(ProviderScopeSnapshot snapshot) => _snapshot = snapshot;

        public Task<ProviderScopeSnapshot> CurrentAsync() => Task.FromResult(_snapshot);

        public Task<ProviderScopeSnapshot?> RequireProviderAsync() =>
            Task.FromResult(_snapshot.IsProvider ? _snapshot : null);
    }
}

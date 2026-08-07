using Web.Models.Enums;
using Web.Services.Inventory;
using Xunit;

namespace Tests;

/// <summary>
/// Núcleo del inventario multi-bodega (épica E1). <see cref="StockLedger"/> es puro, así
/// que estos tests cubren la aritmética y las reglas SIN base de datos.
///
/// Lo que NO se puede probar aquí (queda anotado como deuda en el resumen del incremento):
/// el reintento por <c>RowVersion</c> y la idempotencia de la doble cancelación, porque
/// viven en <see cref="InventoryService"/> y requieren un proveedor EF real.
/// </summary>
public class StockLedgerTests
{
    // ------------------------------------------------------------------ Ventas

    [Fact]
    public void Vender_saca_unidades_de_la_bodega()
    {
        var saldo = new StockBalance(OnHand: 10, Reserved: 0);

        var resultado = StockLedger.Apply(saldo, StockMovementType.Sale, -3);

        Assert.Equal(7, resultado.OnHand);
        Assert.Equal(0, resultado.Reserved);
        Assert.Equal(7, resultado.Available);
    }

    [Fact]
    public void Vender_lo_reservado_no_descuenta_el_disponible_dos_veces()
    {
        // Se reservaron 3 al crear el pedido: el disponible ya bajó a 7.
        var saldo = StockLedger.Reserve(new StockBalance(10, 0), 3);
        Assert.Equal(7, saldo.Available);

        // Al pagarse, la reserva se convierte en salida real. El disponible NO vuelve a bajar.
        var vendido = StockLedger.Apply(saldo, StockMovementType.Sale, -3);

        Assert.Equal(7, vendido.OnHand);
        Assert.Equal(0, vendido.Reserved);
        Assert.Equal(7, vendido.Available);
    }

    [Fact]
    public void Cancelar_una_venta_devuelve_las_unidades_al_disponible()
    {
        var vendido = StockLedger.Apply(new StockBalance(10, 0), StockMovementType.Sale, -4);

        var cancelado = StockLedger.Apply(vendido, StockMovementType.SaleCancelled, 4);

        Assert.Equal(10, cancelado.OnHand);
        Assert.Equal(10, cancelado.Available);
    }

    [Fact]
    public void No_se_puede_dejar_el_inventario_en_negativo()
    {
        var saldo = new StockBalance(2, 0);

        var ex = Assert.Throws<InventoryException>(
            () => StockLedger.Apply(saldo, StockMovementType.Sale, -5));

        Assert.Contains("negativo", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------ Validaciones

    [Fact]
    public void Un_movimiento_de_cero_unidades_no_tiene_sentido()
    {
        Assert.Throws<InventoryException>(
            () => StockLedger.ValidateMovement(StockMovementType.Reception, 0, reason: null));
    }

    [Theory]
    [InlineData(StockMovementType.Adjustment)]
    [InlineData(StockMovementType.Loss)]
    public void Ajuste_y_perdida_exigen_razon(StockMovementType type)
    {
        Assert.Throws<InventoryException>(() => StockLedger.ValidateMovement(type, -1, reason: null));
        Assert.Throws<InventoryException>(() => StockLedger.ValidateMovement(type, -1, reason: "   "));

        // Con razón, pasa.
        StockLedger.ValidateMovement(type, -1, reason: "Conteo físico de bodega");
    }

    [Theory]
    [InlineData(StockMovementType.Reception)]
    [InlineData(StockMovementType.Sale)]
    [InlineData(StockMovementType.SaleCancelled)]
    [InlineData(StockMovementType.CustomerReturn)]
    [InlineData(StockMovementType.TransferIn)]
    [InlineData(StockMovementType.TransferOut)]
    [InlineData(StockMovementType.ReturnToProvider)]
    public void El_resto_de_movimientos_no_exige_razon(StockMovementType type)
    {
        Assert.False(StockLedger.RequiresReason(type));
        StockLedger.ValidateMovement(type, 1, reason: null);
    }

    // --------------------------------------------------------------- Reservas

    [Fact]
    public void No_se_puede_reservar_mas_de_lo_disponible()
    {
        var saldo = new StockBalance(OnHand: 5, Reserved: 3); // disponible = 2

        Assert.Throws<InventoryException>(() => StockLedger.Reserve(saldo, 3));

        // Justo lo disponible sí se puede.
        var ok = StockLedger.Reserve(saldo, 2);
        Assert.Equal(0, ok.Available);
        Assert.Equal(5, ok.OnHand); // el físico no se mueve al reservar
    }

    [Fact]
    public void Reservar_cero_o_negativo_se_rechaza()
    {
        var saldo = new StockBalance(5, 0);

        Assert.Throws<InventoryException>(() => StockLedger.Reserve(saldo, 0));
        Assert.Throws<InventoryException>(() => StockLedger.Reserve(saldo, -1));
    }

    [Fact]
    public void Liberar_nunca_deja_la_reserva_en_negativo()
    {
        // Dato ya descuadrado: se pide liberar más de lo reservado.
        var saldo = new StockBalance(OnHand: 10, Reserved: 2);

        var resultado = StockLedger.Release(saldo, 5);

        Assert.Equal(0, resultado.Reserved);
        Assert.Equal(10, resultado.Available);
    }

    [Fact]
    public void Liberar_devuelve_las_unidades_al_disponible()
    {
        var reservado = StockLedger.Reserve(new StockBalance(10, 0), 4);
        Assert.Equal(6, reservado.Available);

        var liberado = StockLedger.Release(reservado, 4);

        Assert.Equal(10, liberado.Available);
        Assert.Equal(10, liberado.OnHand);
    }

    // -------------------------------------------------------------- Invariante

    [Fact]
    public void El_saldo_fisico_es_siempre_la_suma_del_libro_mayor()
    {
        // Secuencia real: recepción, dos ventas, una cancelación, una merma.
        var movimientos = new[]
        {
            (StockMovementType.Reception, 50),
            (StockMovementType.Sale, -4),
            (StockMovementType.Sale, -6),
            (StockMovementType.SaleCancelled, 4),
            (StockMovementType.Loss, -2)
        };

        var saldo = new StockBalance(0, 0);
        foreach (var (tipo, cantidad) in movimientos)
        {
            saldo = StockLedger.Apply(saldo, tipo, cantidad);
        }

        var replay = StockLedger.ReplayOnHand(movimientos.Select(m => m.Item2));

        Assert.Equal(replay, saldo.OnHand);
        Assert.Equal(42, saldo.OnHand);
    }

    [Fact]
    public void La_invariante_se_mantiene_aunque_haya_reservas_por_medio()
    {
        // Reservar NO escribe en el libro mayor: no puede alterar la invariante.
        var saldo = StockLedger.Apply(new StockBalance(0, 0), StockMovementType.Reception, 20);
        saldo = StockLedger.Reserve(saldo, 5);
        saldo = StockLedger.Apply(saldo, StockMovementType.Sale, -5);

        var replay = StockLedger.ReplayOnHand(new[] { 20, -5 });

        Assert.Equal(replay, saldo.OnHand);
        Assert.Equal(15, saldo.OnHand);
        Assert.Equal(0, saldo.Reserved);
    }

    // ------------------------------------------------- Total denormalizado

    [Fact]
    public void El_total_denormalizado_es_el_disponible_de_todas_las_bodegas()
    {
        var bodegas = new[]
        {
            new StockBalance(OnHand: 10, Reserved: 3),  // 7
            new StockBalance(OnHand: 5,  Reserved: 0),  // 5
            new StockBalance(OnHand: 0,  Reserved: 0)   // 0
        };

        Assert.Equal(12, StockLedger.TotalAvailable(bodegas));
    }

    [Fact]
    public void El_total_denormalizado_nunca_es_negativo()
    {
        // Dato corrupto (más reservado que físico): el catálogo no puede mostrar −2.
        var bodegas = new[] { new StockBalance(OnHand: 1, Reserved: 3) };

        Assert.Equal(0, StockLedger.TotalAvailable(bodegas));
    }

    [Fact]
    public void Sin_bodegas_el_total_es_cero()
    {
        Assert.Equal(0, StockLedger.TotalAvailable(Array.Empty<StockBalance>()));
        Assert.Equal(0, StockLedger.ReplayOnHand(Array.Empty<int>()));
    }
}

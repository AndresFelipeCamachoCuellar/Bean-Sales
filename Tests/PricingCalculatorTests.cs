using Web.Services.Pricing;
using Xunit;

namespace Tests;

/// <summary>
/// Núcleo de cálculo de márgenes (épica E2). Clase pura → tests sin base de datos.
/// Convenio: los porcentajes van en BASE 100 (30 = 30 %).
/// </summary>
public class PricingCalculatorTests
{
    // ------------------------------------------------------------ CalculateMargin

    [Fact]
    public void Margen_caso_normal_es_sobre_el_precio_de_venta()
    {
        // Costo 70.000, PVP 100.000 → 30.000 de margen = 30 % del PVP.
        var margin = PricingCalculator.CalculateMargin(price: 100_000m, supplierPrice: 70_000m);

        Assert.True(margin.IsCalculable);
        Assert.Equal(30_000m, margin.Amount);
        Assert.Equal(30m, margin.Percent);
    }

    [Fact]
    public void Margen_con_costo_cero_es_del_cien_por_ciento()
    {
        var margin = PricingCalculator.CalculateMargin(price: 50_000m, supplierPrice: 0m);

        Assert.True(margin.IsCalculable);
        Assert.Equal(50_000m, margin.Amount);
        Assert.Equal(100m, margin.Percent);
    }

    [Fact]
    public void Margen_negativo_cuando_se_vende_por_debajo_del_costo()
    {
        // Vender a 80.000 algo que costó 100.000 → −25 % (sobre el PVP).
        var margin = PricingCalculator.CalculateMargin(price: 80_000m, supplierPrice: 100_000m);

        Assert.True(margin.IsCalculable);
        Assert.Equal(-20_000m, margin.Amount);
        Assert.Equal(-25m, margin.Percent);
    }

    [Fact]
    public void Margen_sin_pvp_no_es_calculable_pero_conserva_el_importe()
    {
        // Producto sin precio fijado: no hay base para el porcentaje.
        var margin = PricingCalculator.CalculateMargin(price: 0m, supplierPrice: 40_000m);

        Assert.False(margin.IsCalculable);
        Assert.Equal(0m, margin.Percent);
        Assert.Equal(-40_000m, margin.Amount);
    }

    [Fact]
    public void Margen_con_pvp_negativo_tampoco_es_calculable()
    {
        var margin = PricingCalculator.CalculateMargin(price: -10m, supplierPrice: 5m);

        Assert.False(margin.IsCalculable);
    }

    // --------------------------------------------------------------- SuggestPrice

    [Fact]
    public void Sugerido_apunta_al_margen_objetivo_y_redondea_hacia_arriba()
    {
        // 70.000 / (1 − 0,30) = 100.000 exacto → sigue en 100.000 con paso de 50.
        Assert.Equal(100_000m, PricingCalculator.SuggestPrice(70_000m, 30m, 50m));
    }

    [Fact]
    public void Sugerido_nunca_redondea_a_la_baja()
    {
        // 35.000 / 0,7 = 50.000 exacto. Con 35.010 sube a 50.014,28… → 50.050 con paso 50.
        var suggested = PricingCalculator.SuggestPrice(35_010m, 30m, 50m);

        Assert.Equal(50_050m, suggested);
        Assert.True(suggested >= 35_010m / 0.7m);
    }

    [Fact]
    public void Sugerido_con_costo_cero_no_existe()
    {
        Assert.Equal(0m, PricingCalculator.SuggestPrice(0m, 30m, 50m));
        Assert.Equal(0m, PricingCalculator.SuggestPrice(-5m, 30m, 50m));
    }

    [Fact]
    public void Sugerido_con_objetivo_del_cien_por_ciento_no_revienta()
    {
        // 1 − 1 = 0 → división por cero. Se devuelve 0 = "sin sugerencia".
        Assert.Equal(0m, PricingCalculator.SuggestPrice(70_000m, 100m, 50m));
        Assert.Equal(0m, PricingCalculator.SuggestPrice(70_000m, 150m, 50m));
    }

    [Fact]
    public void Sugerido_con_objetivo_negativo_no_existe()
    {
        Assert.Equal(0m, PricingCalculator.SuggestPrice(70_000m, -10m, 50m));
    }

    [Fact]
    public void Sugerido_con_paso_invalido_cae_al_paso_por_defecto()
    {
        // 70.000 / 0,7 = 100.000, múltiplo de 50 → mismo resultado con paso 0.
        Assert.Equal(100_000m, PricingCalculator.SuggestPrice(70_000m, 30m, 0m));
        Assert.Equal(100_000m, PricingCalculator.SuggestPrice(70_000m, 30m, -1m));
    }

    [Fact]
    public void Sugerido_con_objetivo_cero_es_el_costo_redondeado()
    {
        Assert.Equal(70_000m, PricingCalculator.SuggestPrice(70_000m, 0m, 50m));
    }

    // ------------------------------------------------------------ IsBelowMinimum

    [Fact]
    public void Justo_en_el_minimo_no_dispara_la_alerta()
    {
        // 85.000 de costo sobre 100.000 de PVP = 15 % exacto.
        Assert.False(PricingCalculator.IsBelowMinimum(100_000m, 85_000m, 15m));
    }

    [Fact]
    public void Un_peso_por_debajo_del_minimo_dispara_la_alerta()
    {
        Assert.True(PricingCalculator.IsBelowMinimum(100_000m, 85_100m, 15m));
    }

    [Fact]
    public void Producto_sin_pvp_siempre_esta_por_revisar()
    {
        // Es justo el caso que la bandeja "Márgenes por revisar" debe sacar a flote.
        Assert.True(PricingCalculator.IsBelowMinimum(0m, 40_000m, 15m));
    }

    [Fact]
    public void Costo_igual_al_pvp_esta_por_debajo_del_minimo()
    {
        // Situación EXACTA en la que quedan todos los productos tras el backfill de AddPricing.
        Assert.True(PricingCalculator.IsBelowMinimum(50_000m, 50_000m, 15m));
    }

    // ------------------------------------------------------------- RoundUpToStep

    [Theory]
    [InlineData(0, 50, 0)]
    [InlineData(-10, 50, 0)]
    [InlineData(1, 50, 50)]
    [InlineData(50, 50, 50)]
    [InlineData(51, 50, 100)]
    [InlineData(12_345, 100, 12_400)]
    public void Redondeo_siempre_hacia_arriba(decimal value, decimal step, decimal expected)
    {
        Assert.Equal(expected, PricingCalculator.RoundUpToStep(value, step));
    }
}

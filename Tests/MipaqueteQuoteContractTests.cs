using Web.Services.Shipping;
using Xunit;

namespace Tests;

/// <summary>
/// Cubre los helpers PUROS del cotizador de mipaquete: los que traducen nuestro modelo
/// al contrato real de POST {BaseUrl}/routes/quoteShipping (validado en vivo, jul-2026)
/// y los que interpretan la respuesta. Nada de HTTP aquí.
/// </summary>
public class MipaqueteQuoteContractTests
{
    // ---------- Códigos de ubicación (DANE de 5 dígitos → locationCode de 8) ----------

    [Theory]
    [InlineData("76001", "76001000")]   // Cali, la bodega de origen
    [InlineData("11001", "11001000")]   // Bogotá D.C.
    [InlineData("5001", "05001000")]    // DANE sin el cero a la izquierda
    [InlineData("76001000", "76001000")] // ya viene en formato mipaquete
    public void ToLocationCode_ConvierteElDaneAlFormatoDeMipaquete(string dane, string esperado)
    {
        Assert.Equal(esperado, MipaqueteShippingQuoteService.ToLocationCode(dane));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-es-un-codigo")]
    public void ToLocationCode_SinDigitosDevuelveVacio(string? valor)
    {
        Assert.Equal(string.Empty, MipaqueteShippingQuoteService.ToLocationCode(valor));
    }

    // ---------- Código de país ("170" = Colombia) ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CountryCodeOrDefault_SinValorUsaColombia(string? valor)
    {
        Assert.Equal("170", MipaqueteShippingQuoteService.CountryCodeOrDefault(valor));
    }

    [Fact]
    public void CountryCodeOrDefault_RespetaElValorConfiguradoYLoRecorta()
    {
        Assert.Equal("484", MipaqueteShippingQuoteService.CountryCodeOrDefault("  484 "));
    }

    // ---------- shippingTime: MINUTOS → días ----------

    [Theory]
    [InlineData(1440, 1)]    // 1 día exacto
    [InlineData(2880, 2)]    // 2 días exactos
    [InlineData(1500, 2)]    // sobra una hora ⇒ se redondea hacia arriba
    [InlineData(60, 1)]      // menos de un día ⇒ piso de 1
    public void ToBusinessDays_ConvierteMinutosADiasRedondeandoHaciaArriba(int minutos, int esperado)
    {
        Assert.Equal(esperado, MipaqueteShippingQuoteService.ToBusinessDays(minutos, fallbackDays: 4));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-10)]
    public void ToBusinessDays_SinDatoUsaElFallbackDeConfiguracion(int? minutos)
    {
        var valor = minutos.HasValue ? (decimal?)minutos.Value : null;

        Assert.Equal(4, MipaqueteShippingQuoteService.ToBusinessDays(valor, fallbackDays: 4));
    }

    [Fact]
    public void ToBusinessDays_TopaLosValoresAbsurdos()
    {
        // 1.000 días de tránsito no es un dato usable: se topa en 60.
        Assert.Equal(60, MipaqueteShippingQuoteService.ToBusinessDays(1_440_000m, fallbackDays: 4));
    }

    // ---------- Logo de la transportadora ----------

    [Fact]
    public void SafeLogoUrl_AceptaUrlAbsolutaHttps()
    {
        Assert.Equal(
            "https://cdn.mipaquete.com/servientrega.png",
            MipaqueteShippingQuoteService.SafeLogoUrl(" https://cdn.mipaquete.com/servientrega.png "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/img/servientrega.png")]                 // relativa
    [InlineData("http://cdn.mipaquete.com/logo.png")]     // sin TLS ⇒ contenido mixto
    [InlineData("javascript:alert(1)")]                   // esquema peligroso
    public void SafeLogoUrl_DescartaLoQueNoSeaHttpsAbsoluto(string? valor)
    {
        Assert.Null(MipaqueteShippingQuoteService.SafeLogoUrl(valor));
    }

    // ---------- Normalización numérica (la API solo acepta enteros) ----------

    [Theory]
    [InlineData(0.4, 1)]     // nunca por debajo de 1: la API rechaza ceros
    [InlineData(1, 1)]
    [InlineData(2.1, 3)]     // siempre hacia arriba (así cobra la transportadora)
    public void ToPositiveInt_RedondeaHaciaArribaConPisoDeUno(double valor, int esperado)
    {
        Assert.Equal(esperado, MipaqueteShippingQuoteService.ToPositiveInt((decimal)valor));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(10000.2, 10001)]
    public void ToNonNegativeLong_NuncaEsNegativoYRedondeaHaciaArriba(double valor, long esperado)
    {
        Assert.Equal(esperado, MipaqueteShippingQuoteService.ToNonNegativeLong((decimal)valor));
    }
}

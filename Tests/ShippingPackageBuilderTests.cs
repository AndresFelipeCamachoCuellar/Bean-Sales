using Web.Models;
using Web.Services.Shipping;
using Xunit;

namespace Tests;

/// <summary>
/// Primeros tests unitarios del proyecto. ShippingPackageBuilder es puro (sin EF ni HTTP),
/// así que cubre el 90% del riesgo lógico del cálculo de envío sin infraestructura.
/// </summary>
public class ShippingPackageBuilderTests
{
    private const string Origin = "11001";   // Bogotá D.C.
    private const string Destination = "05001"; // Medellín

    private static ShippingDefaultsOptions Defaults() => new();

    private static ShoppingCartItem Item(
        int quantity,
        int? weightGrams = null,
        decimal? length = null,
        decimal? width = null,
        decimal? height = null,
        decimal price = 0m)
    {
        return new ShoppingCartItem
        {
            ShoppingCartItemID = Guid.NewGuid(),
            ProductID = Guid.NewGuid(),
            Quantity = quantity,
            Product = new Product
            {
                ProductID = Guid.NewGuid(),
                Name = "Lote de prueba",
                Price = price,
                ShippingWeightGrams = weightGrams,
                LengthCm = length,
                WidthCm = width,
                HeightCm = height
            }
        };
    }

    [Fact]
    public void UnItemConDatosCompletos_UsaSusPropiasMedidas()
    {
        var items = new[] { Item(1, 500, 20m, 12m, 8m, price: 30000m) };

        var package = ShippingPackageBuilder.Build(items, Origin, Destination, Defaults());

        Assert.Equal(0.5m, package.RawWeightKg);
        Assert.Equal(1m, package.Request.WeightKg);      // mínimo facturable
        Assert.Equal(20m, package.Request.LengthCm);     // el ítem es más largo que el cubo
        Assert.Equal(15m, package.Request.WidthCm);      // clamp de caja mínima
        Assert.Equal(10m, package.Request.HeightCm);     // clamp de caja mínima
        Assert.Equal(30000m, package.Request.DeclaredValue);
        Assert.False(package.ExceedsMaxWeight);
        Assert.Equal(0, package.ItemsWithoutWeight);
        Assert.Equal(0, package.ItemsWithoutDimensions);
        Assert.Equal(Origin, package.Request.OriginDaneCode);
        Assert.Equal(Destination, package.Request.DestinationDaneCode);
        Assert.Equal("CO", package.Request.CountryCode);
    }

    [Fact]
    public void VariosItems_SumaPesoYValorDeclarado()
    {
        var items = new[]
        {
            Item(2, 500, 20m, 12m, 8m, price: 30000m),
            Item(1, 1000, 25m, 15m, 10m, price: 60000m)
        };

        var package = ShippingPackageBuilder.Build(items, Origin, Destination, Defaults());

        Assert.Equal(2m, package.RawWeightKg);            // 2×500 g + 1×1000 g
        Assert.Equal(2m, package.Request.WeightKg);
        Assert.Equal(120000m, package.Request.DeclaredValue);

        // La caja no se estira en columna: crece como cubo, pero nunca por debajo
        // de la pieza más grande.
        Assert.True(package.Request.LengthCm >= 25m);
        Assert.True(package.Request.WidthCm >= 15m);
        Assert.True(package.Request.HeightCm >= 10m);
    }

    [Fact]
    public void ProductoSinPeso_AplicaElDefaultDeConfiguracion()
    {
        var items = new[] { Item(3, weightGrams: null, price: 25000m) };

        var package = ShippingPackageBuilder.Build(items, Origin, Destination, Defaults());

        Assert.Equal(1.5m, package.RawWeightKg);  // 3 × 500 g por defecto
        Assert.Equal(1.5m, package.Request.WeightKg);
        Assert.Equal(1, package.ItemsWithoutWeight);
        Assert.Equal(1, package.ItemsWithoutDimensions);
    }

    [Fact]
    public void PesoPorEncimaDelMaximo_SeMarcaComoExcedido()
    {
        var items = new[] { Item(31, 1000, 20m, 12m, 8m, price: 30000m) };

        var package = ShippingPackageBuilder.Build(items, Origin, Destination, Defaults());

        Assert.Equal(31m, package.RawWeightKg);
        Assert.True(package.ExceedsMaxWeight);
    }

    [Fact]
    public void PesoJustoEnElMaximo_NoSeMarcaComoExcedido()
    {
        var items = new[] { Item(30, 1000, 20m, 12m, 8m) };

        var package = ShippingPackageBuilder.Build(items, Origin, Destination, Defaults());

        Assert.Equal(30m, package.RawWeightKg);
        Assert.False(package.ExceedsMaxWeight);
    }

    [Fact]
    public void PesoPorDebajoDelMinimoFacturable_SeElevaAUnKilo()
    {
        var items = new[] { Item(1, 200, 10m, 10m, 5m) };

        var package = ShippingPackageBuilder.Build(items, Origin, Destination, Defaults());

        Assert.Equal(0.2m, package.RawWeightKg);
        Assert.Equal(1m, package.Request.WeightKg);
    }

    [Fact]
    public void ItemDiminuto_SeAjustaALaCajaMinima()
    {
        var items = new[] { Item(1, 10, 1m, 1m, 1m) };

        var package = ShippingPackageBuilder.Build(items, Origin, Destination, Defaults());

        Assert.Equal(15m, package.Request.LengthCm);
        Assert.Equal(15m, package.Request.WidthCm);
        Assert.Equal(10m, package.Request.HeightCm);
    }

    [Fact]
    public void FactorDeEmpaque_AumentaElVolumenDeLaCaja()
    {
        var items = new[] { Item(1, 5000, 30m, 30m, 30m) };

        var sinHolgura = ShippingPackageBuilder.Build(
            items, Origin, Destination, new ShippingDefaultsOptions { PackingFactor = 1m });

        var conHolgura = ShippingPackageBuilder.Build(
            items, Origin, Destination, new ShippingDefaultsOptions { PackingFactor = 2m });

        var volumenSin = sinHolgura.Request.LengthCm * sinHolgura.Request.WidthCm * sinHolgura.Request.HeightCm;
        var volumenCon = conHolgura.Request.LengthCm * conHolgura.Request.WidthCm * conHolgura.Request.HeightCm;

        Assert.True(volumenCon > volumenSin);

        // Sin holgura la caja sigue conteniendo la pieza (30 × 30 × 30).
        Assert.True(sinHolgura.Request.LengthCm >= 30m);
        Assert.True(sinHolgura.Request.WidthCm >= 30m);
        Assert.True(sinHolgura.Request.HeightCm >= 30m);
    }

    [Fact]
    public void CarritoVacio_DevuelveUnPaqueteMinimoValido()
    {
        var package = ShippingPackageBuilder.Build(
            Array.Empty<ShoppingCartItem>(), Origin, Destination, Defaults());

        Assert.Equal(0m, package.RawWeightKg);
        Assert.Equal(1m, package.Request.WeightKg);
        Assert.Equal(15m, package.Request.LengthCm);
        Assert.Equal(15m, package.Request.WidthCm);
        Assert.Equal(10m, package.Request.HeightCm);
        Assert.Equal(0m, package.Request.DeclaredValue);
        Assert.False(package.ExceedsMaxWeight);
    }

    [Fact]
    public void CantidadNoPositiva_SeIgnora()
    {
        var items = new[]
        {
            Item(0, 500, 20m, 12m, 8m, price: 30000m),
            Item(2, 500, 20m, 12m, 8m, price: 30000m)
        };

        var package = ShippingPackageBuilder.Build(items, Origin, Destination, Defaults());

        Assert.Equal(1m, package.RawWeightKg);        // solo la línea de cantidad 2
        Assert.Equal(60000m, package.Request.DeclaredValue);
    }
}

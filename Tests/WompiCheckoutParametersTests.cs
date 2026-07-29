using Web.Services.Payments;
using Xunit;

namespace Tests;

/// <summary>
/// Blindaje del bug bloqueante encontrado en QA (jul-2026): Wompi rechazaba la transacción
/// con «Parámetro "shipping-address:phone-number" no proveído» porque el bloque
/// <c>shipping-address:*</c> se enviaba SIN el celular.
///
/// Reglas que se verifican aquí (doc: docs.wompi.co/docs/colombia/widget-checkout-web,
/// Paso 5 · Parámetros opcionales):
///  · el bloque de envío es TODO-O-NADA: address-line-1, city, region, country Y phone-number;
///  · <c>customer-data:phone-number</c> siempre viaja con su <c>phone-number-prefix</c>;
///  · el celular llega a la pasarela como dígitos, sin indicativo duplicado.
/// </summary>
public class WompiCheckoutParametersTests
{
    // ------------------------------------------------------- Normalización

    [Theory]
    [InlineData("3001234567", "3001234567")]      // celular tal cual
    [InlineData("300 123 4567", "3001234567")]    // con espacios
    [InlineData("(300) 123-4567", "3001234567")]  // con separadores
    [InlineData("+57 300 123 4567", "3001234567")] // con indicativo: se quita para no duplicarlo
    [InlineData("0057 3001234567", "3001234567")]  // marcación internacional antigua
    [InlineData("6024567", "6024567")]             // fijo de 7 dígitos
    public void NormalizePhone_DejaSoloDigitosYQuitaElIndicativo(string entrada, string esperado)
    {
        Assert.Equal(esperado, PaymentContact.NormalizePhone(entrada));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sin numeros")]
    [InlineData("12345")]                       // demasiado corto
    [InlineData("1234567890123456789")]         // demasiado largo
    public void NormalizePhone_DevuelveNullSiNoEsUnNumeroPlausible(string? entrada)
    {
        Assert.Null(PaymentContact.NormalizePhone(entrada));
    }

    // --------------------------------------------- Bloque shipping-address

    [Fact]
    public void CanSendShippingAddress_EsTrueConLosCincoCamposObligatorios()
    {
        Assert.True(Build().CanSendShippingAddress);
    }

    [Theory]
    [InlineData("phone")]
    [InlineData("address")]
    [InlineData("city")]
    [InlineData("region")]
    [InlineData("country")]
    public void CanSendShippingAddress_EsFalseSiFaltaCualquierObligatorio(string faltante)
    {
        // Todo-o-nada: enviar el bloque incompleto es exactamente lo que Wompi rechaza.
        var checkout = Build() with
        {
            ShippingPhone = faltante == "phone" ? null : "3001234567",
            ShippingAddressLine1 = faltante == "address" ? null : "Calle 123 #45-67",
            ShippingCity = faltante == "city" ? null : "Cali",
            ShippingRegion = faltante == "region" ? null : "Valle del Cauca",
            ShippingCountry = faltante == "country" ? null : "CO"
        };

        Assert.False(checkout.CanSendShippingAddress);
    }

    // ------------------------------------------------ Bloque customer-data

    [Fact]
    public void CanSendCustomerPhone_ExigeTelefonoYPrefijoJuntos()
    {
        Assert.True(Build().CanSendCustomerPhone);
        Assert.False((Build() with { CustomerPhone = null }).CanSendCustomerPhone);
        Assert.False((Build() with { CustomerPhonePrefix = null }).CanSendCustomerPhone);
    }

    /// <summary>Pedido "bien formado": todos los campos que la vista puede pintar.</summary>
    private static PaymentCheckoutRequest Build() => new(
        CheckoutUrl: "https://checkout.wompi.co/p/",
        PublicKey: "pub_test_ejemplo",
        Currency: "COP",
        AmountInCents: 19_000_000,
        Reference: "BEAN-abc-1",
        IntegritySignature: "hash",
        RedirectUrl: "https://kingbean.runasp.net/pagos/resultado/abc",
        ExpirationTimeIso: null,
        CustomerEmail: "cliente@example.com",
        CustomerFullName: "Andrés Camacho",
        CustomerPhone: "3001234567",
        CustomerPhonePrefix: "+57",
        ShippingAddressLine1: "Calle 123 #45-67",
        ShippingCity: "Cali",
        ShippingRegion: "Valle del Cauca",
        ShippingCountry: "CO",
        ShippingPhone: "3001234567",
        ShippingName: "Andrés Camacho",
        OrderID: Guid.NewGuid(),
        AmountInPesos: 190_000m);
}

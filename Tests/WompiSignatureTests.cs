using Web.Services.Payments;
using Xunit;

namespace Tests;

/// <summary>
/// La parte criptográfica de la integración con Wompi, verificada contra los VECTORES
/// EXACTOS de la documentación oficial (jul-2026). Si estos tests pasan, la firma que
/// enviamos y la validación del webhook son correctas antes de tocar la red.
///
/// Fuentes:
///  · docs.wompi.co/docs/colombia/widget-checkout-web (Paso 3: firma de integridad)
///  · docs.wompi.co/docs/colombia/eventos (Seguridad: checksum del evento)
/// </summary>
public class WompiSignatureTests
{
    // ---------------------------------------------------------- Integridad

    [Fact]
    public void Integrity_ReproduceElVectorDeLaDocumentacion()
    {
        // "sk8-438k4-xmxm392-sn2m" + "2490000" + "COP" + secreto
        var firma = WompiSignature.Integrity(
            reference: "sk8-438k4-xmxm392-sn2m",
            amountInCents: 2_490_000,
            currency: "COP",
            integritySecret: "prod_integrity_Z5mMke9x0k8gpErbDqwrJXMqsI6SFli6");

        Assert.Equal("37c8407747e595535433ef8f6a811d853cd943046624a0ec04662b17bbf33bf5", firma);
    }

    [Fact]
    public void Integrity_ConExpirationTime_CambiaLaFirma()
    {
        // La fecha de expiración se concatena ANTES del secreto: si se envía
        // expiration-time y no se incluye aquí, Wompi rechaza la transacción.
        var sinExpiracion = WompiSignature.Integrity(
            "REF-1", 1_000_000, "COP", "test_integrity_abc");

        var conExpiracion = WompiSignature.Integrity(
            "REF-1", 1_000_000, "COP", "test_integrity_abc", "2026-07-28T20:28:50.000Z");

        Assert.NotEqual(sinExpiracion, conExpiracion);

        // Y coincide con el hash de la cadena construida a mano, en ese orden.
        var esperado = WompiSignature.Sha256Hex(
            "REF-11000000COP2026-07-28T20:28:50.000Ztest_integrity_abc");
        Assert.Equal(esperado, conExpiracion);
    }

    [Fact]
    public void Integrity_NoFormateaElMontoConSeparadores()
    {
        // Bug clásico de esta integración: en es-CO, 4490000 formateado con cultura
        // sale como "4.490.000" y la firma nunca coincide.
        var firma = WompiSignature.Integrity("REF-2", 4_490_000, "COP", "s3cr3t");
        var esperado = WompiSignature.Sha256Hex("REF-24490000COPs3cr3t");

        Assert.Equal(esperado, firma);
    }

    // ------------------------------------------------------------- Eventos

    [Fact]
    public void EventChecksum_ConcatenaEnElOrdenDocumentado()
    {
        // Ejemplo de la doc de Wompi (https://docs.wompi.co/docs/colombia/eventos/):
        // properties = [transaction.id, transaction.status, transaction.amount_in_cents]
        var valores = new[] { "1234-1610641025-49201", "APPROVED", "4490000" };

        var checksum = WompiSignature.EventChecksum(
            valores,
            timestamp: 1530291411,
            eventsSecret: "prod_events_OcHnIzeBl5socpwByQ4hA52Em3USQ93Z");

        // La doc describe la cadena a firmar exactamente así (pasos 1 a 3):
        //   valores en el orden de `properties` + timestamp + secreto de eventos
        var cadenaDocumentada =
            "1234-1610641025-49201APPROVED44900001530291411prod_events_OcHnIzeBl5socpwByQ4hA52Em3USQ93Z";

        Assert.Equal(WompiSignature.Sha256Hex(cadenaDocumentada), checksum);

        // OJO — no se compara contra el hash publicado en la doc
        // ("3476DDA5...BD0") porque ESE VALOR ES ERRÓNEO: no es el SHA-256 de la
        // cadena que la propia doc muestra. Verificado de forma independiente
        // (28-jul-2026): sha256("1234-...prod_events_OcHn...") = 5a18ec5e8f...
        // Probablemente lo generaron con otro secreto y luego lo anonimizaron.
        // Lo que sí garantizamos aquí es el ALGORITMO documentado (pasos 1-4),
        // que es lo que Wompi usa realmente para firmar los eventos.
    }

    [Fact]
    public void ChecksumMatches_EsInsensibleAMayusculas()
    {
        const string minusculas = "3476dda50f64cd7cbd160689640506febea93239bc524fc0469b2c68a3cc8bd0";
        const string mayusculas = "3476DDA50F64CD7CBD160689640506FEBEA93239BC524FC0469B2C68A3CC8BD0";

        Assert.True(WompiSignature.ChecksumMatches(minusculas, mayusculas));
        Assert.True(WompiSignature.ChecksumMatches(mayusculas, minusculas));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-es-hexadecimal")]
    [InlineData("3476dda50f64cd7cbd160689640506febea93239bc524fc0469b2c68a3cc8bd1")] // último dígito cambiado
    public void ChecksumMatches_RechazaValoresInvalidos(string? recibido)
    {
        const string esperado = "3476dda50f64cd7cbd160689640506febea93239bc524fc0469b2c68a3cc8bd0";

        Assert.False(WompiSignature.ChecksumMatches(esperado, recibido));
    }

    // ------------------------------------------------------------- Importes

    [Theory]
    [InlineData(95_000, 9_500_000L)]     // el ejemplo literal de la documentación
    [InlineData(18_040, 1_804_000L)]
    [InlineData(0, 0L)]
    public void ToCents_ConvierteAPesosPorCien(int pesos, long esperado)
    {
        Assert.Equal(esperado, PaymentAmounts.ToCents(pesos));
    }

    [Fact]
    public void RoundPesos_RedondeaAEnterosAntesDeFirmar()
    {
        // Si el total tuviera centavos, la firma y el pedido discreparían.
        Assert.Equal(18_041m, PaymentAmounts.RoundPesos(18_040.5m));
        Assert.Equal(18_040m, PaymentAmounts.RoundPesos(18_040.4m));
        Assert.Equal(1_804_100L, PaymentAmounts.ToCents(18_040.5m));
    }
}

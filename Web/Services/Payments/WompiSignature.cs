using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Web.Services.Payments;

/// <summary>
/// Criptografía de Wompi: estática, pura y sin dependencias (por eso es testeable sin red).
///
/// ─────────────────────────────────────────────────────────────────────────────
/// CONTRATO CONFIRMADO contra la documentación oficial (jul-2026)
/// ─────────────────────────────────────────────────────────────────────────────
/// 1) FIRMA DE INTEGRIDAD — docs.wompi.co/docs/colombia/widget-checkout-web (Paso 3)
///    SHA-256 hex de la concatenación, EN ESTE ORDEN:
///        "&lt;Referencia&gt;&lt;MontoEnCentavos&gt;&lt;Moneda&gt;&lt;SecretoIntegridad&gt;"
///    y, SI se envía expiration-time, la fecha ISO-8601 va ANTES del secreto:
///        "&lt;Referencia&gt;&lt;Monto&gt;&lt;Moneda&gt;&lt;FechaExpiracion&gt;&lt;SecretoIntegridad&gt;"
///    Vector de la doc:
///        "sk8-438k4-xmxm392-sn2m2490000COPprod_integrity_Z5mMke9x0k8gpErbDqwrJXMqsI6SFli6"
///        => 37c8407747e595535433ef8f6a811d853cd943046624a0ec04662b17bbf33bf5
///    Se envía en el parámetro literal "signature:integrity" y SIEMPRE se calcula en el
///    servidor: es lo que impide que alguien edite amount-in-cents desde DevTools.
///
/// 2) CHECKSUM DE EVENTOS — docs.wompi.co/docs/colombia/eventos (Seguridad)
///    SHA-256 hex de: valores de los campos listados en signature.properties (EN ESE
///    ORDEN, recorridos dinámicamente porque la doc avisa que la lista puede cambiar)
///    + timestamp (entero) + secreto de eventos.
///    Vector de la doc:
///        "1234-1610641025-49201APPROVED44900001530291411prod_events_OcHnIzeBl5socpwByQ4hA52Em3USQ93Z"
///        => 3476DDA50F64CD7CBD160689640506FEBEA93239BC524FC0469B2C68A3CC8BD0
///    Llega duplicado en el header "X-Event-Checksum" y en signature.checksum.
///    ⚠️ El ejemplo de integridad está en minúsculas y el de eventos en MAYÚSCULAS:
///       la comparación es SIEMPRE case-insensitive.
///
/// ⚠️ NUNCA formatear números con cultura al construir estas cadenas: en es-CO
///    4490000 se convertiría en "4.490.000" y la firma jamás coincidiría.
/// </summary>
public static class WompiSignature
{
    /// <summary>
    /// Firma de integridad de la transacción.
    /// </summary>
    /// <param name="reference">Referencia única de pago.</param>
    /// <param name="amountInCents">Monto en centavos (COP no tiene decimales: pesos × 100).</param>
    /// <param name="currency">Moneda (hoy solo "COP").</param>
    /// <param name="integritySecret">Secreto de integridad del comercio.</param>
    /// <param name="expirationTimeIso">
    /// Fecha de expiración ISO-8601 UTC (ej. "2026-07-28T20:28:50.000Z"). Si se envía a
    /// Wompi el parámetro expiration-time, DEBE entrar también aquí o la firma no cuadra.
    /// </param>
    public static string Integrity(
        string reference,
        long amountInCents,
        string currency,
        string integritySecret,
        string? expirationTimeIso = null)
    {
        var raw = new StringBuilder()
            .Append(reference)
            .Append(amountInCents.ToString(CultureInfo.InvariantCulture))
            .Append(currency);

        if (!string.IsNullOrWhiteSpace(expirationTimeIso))
        {
            raw.Append(expirationTimeIso);
        }

        raw.Append(integritySecret);

        return Sha256Hex(raw.ToString());
    }

    /// <summary>
    /// Checksum de un evento: valores de <c>signature.properties</c> (en orden) + timestamp + secreto.
    /// </summary>
    public static string EventChecksum(IEnumerable<string> propertyValues, long timestamp, string eventsSecret)
    {
        var raw = new StringBuilder();

        foreach (var value in propertyValues)
        {
            raw.Append(value);
        }

        raw.Append(timestamp.ToString(CultureInfo.InvariantCulture));
        raw.Append(eventsSecret);

        return Sha256Hex(raw.ToString());
    }

    /// <summary>
    /// Compara dos checksums en hexadecimal: insensible a mayúsculas (la doc mezcla
    /// ambos formatos) y en tiempo constante (no filtra información por temporización).
    /// </summary>
    public static bool ChecksumMatches(string? expected, string? received)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(received))
        {
            return false;
        }

        var a = expected.Trim();
        var b = received.Trim();

        if (a.Length != b.Length)
        {
            return false;
        }

        try
        {
            // Convert.FromHexString acepta may/min indistintamente: normaliza el caso
            // sin exponer una comparación de texto sensible a temporización.
            var expectedBytes = Convert.FromHexString(a);
            var receivedBytes = Convert.FromHexString(b);

            return CryptographicOperations.FixedTimeEquals(expectedBytes, receivedBytes);
        }
        catch (FormatException)
        {
            // El valor recibido no es hexadecimal: no es de Wompi.
            return false;
        }
    }

    /// <summary>SHA-256 en hexadecimal minúscula del texto UTF-8.</summary>
    public static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

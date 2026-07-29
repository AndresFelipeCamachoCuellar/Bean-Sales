using Web.Models;
using Web.Models.Enums;

namespace Web.Services.Payments;

/// <summary>
/// Abstracción de la pasarela. Aísla a Wompi para que (a) migrar del Web Checkout al
/// widget toque solo la vista y (b) al abrir mercado fuera de Colombia se pueda sumar
/// una segunda pasarela en USD/CAD sin tocar el ciclo de venta (Wompi solo maneja COP).
/// </summary>
public interface IPaymentGateway
{
    /// <summary>¿Hay llaves cargadas y el cobro está habilitado? Si no, el checkout degrada al modo manual.</summary>
    bool CanCharge { get; }

    /// <summary>Referencia única e irrepetible para un intento de pago de un pedido.</summary>
    string BuildReference(Guid orderId, int attempt);

    /// <summary>Resuelve una referencia emitida por nosotros de vuelta a su pedido (la usa el webhook).</summary>
    bool TryParseReference(string? reference, out Guid orderId, out int attempt);

    /// <summary>
    /// Parámetros firmados del Web Checkout. TODO sale del servidor: el navegador solo
    /// es el vehículo que hace el GET a checkout.wompi.co.
    /// </summary>
    PaymentCheckoutRequest BuildCheckout(Order order, string redirectUrl, string? customerEmail);

    /// <summary>
    /// Consulta el estado de una transacción por API (respaldo síncrono del webhook).
    /// NUNCA lanza: ante cualquier fallo devuelve null y el webhook resuelve.
    /// </summary>
    Task<PaymentSnapshot?> GetTransactionAsync(string transactionId, CancellationToken ct = default);
}

/// <summary>
/// Todo lo que la vista de redirección necesita pintar en el formulario. Contiene la
/// llave PÚBLICA únicamente: ningún secreto llega al navegador.
/// </summary>
public sealed record PaymentCheckoutRequest(
    string CheckoutUrl,
    string PublicKey,
    string Currency,
    long AmountInCents,
    string Reference,
    string IntegritySignature,
    string RedirectUrl,
    string? ExpirationTimeIso,
    string? CustomerEmail,
    string? CustomerFullName,
    string? CustomerPhone,
    string? CustomerPhonePrefix,
    string? ShippingAddressLine1,
    string? ShippingCity,
    string? ShippingRegion,
    string? ShippingCountry,
    string? ShippingPhone,
    string? ShippingName,
    Guid OrderID,
    decimal AmountInPesos)
{
    /// <summary>
    /// ¿Se puede enviar el bloque <c>shipping-address:*</c>?
    ///
    /// Wompi trata el bloque como TODO-O-NADA: si se envía alguno de sus subcampos
    /// obligatorios (<c>address-line-1</c>, <c>city</c>, <c>region</c>, <c>country</c> y
    /// <c>phone-number</c>) exige los cinco, y si falta uno rechaza la transacción con
    /// "Parámetro «shipping-address:phone-number» no proveído".
    ///
    /// El bloque completo es OPCIONAL, así que ante cualquier dato faltante se omite
    /// entero: es preferible perder el prellenado de la dirección en la pasarela a
    /// bloquear el cobro.
    /// </summary>
    public bool CanSendShippingAddress =>
        !string.IsNullOrWhiteSpace(ShippingAddressLine1)
        && !string.IsNullOrWhiteSpace(ShippingCity)
        && !string.IsNullOrWhiteSpace(ShippingRegion)
        && !string.IsNullOrWhiteSpace(ShippingCountry)
        && !string.IsNullOrWhiteSpace(ShippingPhone);

    /// <summary>
    /// ¿Se puede enviar el teléfono del pagador? La doc exige que
    /// <c>customer-data:phone-number</c> viaje SIEMPRE acompañado de
    /// <c>customer-data:phone-number-prefix</c>; sin el prefijo se omiten los dos.
    /// </summary>
    public bool CanSendCustomerPhone =>
        !string.IsNullOrWhiteSpace(CustomerPhone)
        && !string.IsNullOrWhiteSpace(CustomerPhonePrefix);
}

/// <summary>
/// Foto del estado de una transacción, venga del webhook o de la consulta al API.
/// Ambas fuentes se aplican con el MISMO servicio idempotente.
/// </summary>
public sealed record PaymentSnapshot(
    string Reference,
    string? TransactionId,
    PaymentStatus Status,
    long? AmountInCents,
    string? PaymentMethodType,
    string? StatusMessage,
    string? Environment,
    string? Currency,
    string Source);

/// <summary>
/// Datos de contacto que exige cualquier pasarela/transportadora. Vive aquí (y no en la
/// implementación de Wompi) para que el checkout valide el celular con la MISMA regla que
/// se usará al cobrar, sin acoplarse a una pasarela concreta.
/// </summary>
public static class PaymentContact
{
    /// <summary>Indicativo país por defecto: Wompi solo cobra en COP y hoy solo se despacha Colombia.</summary>
    public const string DefaultPhonePrefix = "+57";

    /// <summary>
    /// Deja el celular en solo dígitos (las pasarelas no aceptan espacios ni guiones) y le
    /// quita el indicativo 57 si el cliente lo escribió, para no duplicarlo con el prefijo.
    /// Devuelve <c>null</c> si no queda un número plausible: así el llamador puede rechazar
    /// el formulario o, en el peor caso, omitir el bloque de datos incompleto.
    /// </summary>
    public static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());

        // "+57 301 234 5678" / "0057 301..." -> "3012345678"
        if (digits.Length == 12 && digits.StartsWith("57", StringComparison.Ordinal))
        {
            digits = digits.Substring(2);
        }
        else if (digits.Length == 14 && digits.StartsWith("0057", StringComparison.Ordinal))
        {
            digits = digits.Substring(4);
        }

        // Un fijo nacional tiene 7 dígitos; el tope evita mandar basura a la pasarela.
        if (digits.Length < 7 || digits.Length > 15) return null;

        return digits;
    }
}

/// <summary>Conversión de importes. COP no tiene decimales: el monto va en centavos = pesos × 100.</summary>
public static class PaymentAmounts
{
    /// <summary>
    /// Redondea a pesos enteros. Se aplica ANTES de firmar y de persistir el total, para
    /// que la firma y el pedido no discrepen en centavos.
    /// </summary>
    public static decimal RoundPesos(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);

    /// <summary>Pesos -> centavos. La doc: "si deseas cobrar $95.000 COP, ingresa 9500000".</summary>
    public static long ToCents(decimal pesos) => (long)(RoundPesos(pesos) * 100m);
}

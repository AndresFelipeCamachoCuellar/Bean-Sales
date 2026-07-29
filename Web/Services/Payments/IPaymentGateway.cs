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
    string? ShippingAddressLine1,
    string? ShippingCity,
    string? ShippingRegion,
    string? ShippingCountry,
    Guid OrderID,
    decimal AmountInPesos);

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

namespace Web.Services.Pricing;

/// <summary>
/// Resultado de calcular el margen de un lote.
/// </summary>
/// <param name="Amount">Margen en pesos: <c>Price − SupplierPrice</c>. Puede ser negativo.</param>
/// <param name="Percent">
/// Margen porcentual sobre el PVP: <c>(Price − SupplierPrice) / Price × 100</c>.
/// Vale 0 cuando no es calculable (ver <paramref name="IsCalculable"/>).
/// </param>
/// <param name="IsCalculable">
/// false cuando el PVP es 0 o negativo: no existe "porcentaje sobre el precio de venta".
/// En ese caso <paramref name="Amount"/> sigue siendo válido, pero el % no significa nada
/// y la UI debe mostrar "—" en vez de "0 %".
/// </param>
public readonly record struct PricingMargin(decimal Amount, decimal Percent, bool IsCalculable);

/// <summary>
/// Núcleo de cálculo de precios y márgenes. Clase PURA: sin EF, sin HTTP, sin estado y
/// sin dependencias del contenedor — mismo patrón que <c>ShippingPackageBuilder</c> y
/// <c>WompiSignature</c>, para poder probarla entera con xUnit.
///
/// Convenio de unidades: los porcentajes viajan en BASE 100 (30 = 30 %), igual que se
/// guardan en <see cref="Web.Models.PricingSettings"/> y que los ve el usuario.
/// </summary>
public static class PricingCalculator
{
    /// <summary>Redondeo por defecto cuando la configuración trae un paso inválido (≤ 0).</summary>
    public const decimal DefaultRoundingStep = 50m;

    /// <summary>
    /// Margen = (PVP − costo) / PVP. Se expresa sobre el PRECIO DE VENTA (no sobre el
    /// costo): es el criterio del plan de negocio y el que hace comparable el 30 %/15 %
    /// con la comisión de la pasarela, que también se cobra sobre el total facturado.
    /// </summary>
    public static PricingMargin CalculateMargin(decimal price, decimal supplierPrice)
    {
        var amount = price - supplierPrice;

        // Sin PVP positivo no hay base sobre la que sacar un porcentaje. Se devuelve el
        // importe (que sí tiene sentido) y se marca el % como no calculable.
        if (price <= 0m)
        {
            return new PricingMargin(amount, 0m, false);
        }

        var percent = amount / price * 100m;

        return new PricingMargin(
            decimal.Round(amount, 2, MidpointRounding.AwayFromZero),
            decimal.Round(percent, 2, MidpointRounding.AwayFromZero),
            true);
    }

    /// <summary>
    /// Precio sugerido para alcanzar el margen objetivo:
    /// <c>techo(costo / (1 − objetivo), paso)</c>. Redondeo HACIA ARRIBA al múltiplo del
    /// paso, igual que ya se hace con las tarifas de envío (nunca se redondea a la baja:
    /// eso comería margen).
    ///
    /// Devuelve <c>0</c> cuando NO hay sugerencia posible:
    /// costo ≤ 0, objetivo negativo, u objetivo ≥ 100 % (división por cero: ningún precio
    /// finito deja el 100 % de margen). La UI trata el 0 como "sin sugerencia".
    /// Es una SUGERENCIA: jamás se aplica sola.
    /// </summary>
    public static decimal SuggestPrice(decimal supplierPrice, decimal targetMarginPercent, decimal roundingStep)
    {
        if (supplierPrice <= 0m) return 0m;
        if (targetMarginPercent < 0m) return 0m;
        if (targetMarginPercent >= 100m) return 0m;

        var target = targetMarginPercent / 100m;
        var raw = supplierPrice / (1m - target);

        return RoundUpToStep(raw, roundingStep);
    }

    /// <summary>
    /// true si el margen vigente está POR DEBAJO del mínimo configurado. Es el disparador
    /// de <c>Product.MarginAlert</c> y de la confirmación explícita al guardar un PVP bajo.
    ///
    /// Un PVP de 0 (producto sin precio fijado) cuenta como "bajo el mínimo": es
    /// exactamente el caso que la bandeja "Márgenes por revisar" debe sacar a flote.
    /// </summary>
    public static bool IsBelowMinimum(decimal price, decimal supplierPrice, decimal minimumPercent)
    {
        var margin = CalculateMargin(price, supplierPrice);

        // Sin PVP no hay margen que defender: hay que revisarlo sí o sí.
        if (!margin.IsCalculable) return true;

        return margin.Percent < minimumPercent;
    }

    /// <summary>Redondeo hacia arriba al múltiplo de <paramref name="step"/> (patrón del cotizador de envíos).</summary>
    public static decimal RoundUpToStep(decimal value, decimal step)
    {
        if (value <= 0m) return 0m;

        var effectiveStep = step > 0m ? step : DefaultRoundingStep;

        return Math.Ceiling(value / effectiveStep) * effectiveStep;
    }
}

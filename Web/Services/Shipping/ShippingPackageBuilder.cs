using Web.Models;

namespace Web.Services.Shipping;

/// <summary>
/// Resultado de agregar el carrito en un solo bulto. Además del request para el
/// cotizador expone métricas de diagnóstico (cuántos ítems usaron valores por
/// defecto) para poder medir qué tan sucio está el catálogo.
/// </summary>
public sealed record ShippingPackage
{
    public required ShippingQuoteRequest Request { get; init; }

    /// <summary>Peso real agregado antes de aplicar el mínimo facturable.</summary>
    public required decimal RawWeightKg { get; init; }

    /// <summary>true si el carrito supera Defaults:MaxPackageWeightKg (hay que bloquear y derivar a comercial).</summary>
    public required bool ExceedsMaxWeight { get; init; }

    /// <summary>Nº de líneas cuyo producto no tenía peso cargado.</summary>
    public required int ItemsWithoutWeight { get; init; }

    /// <summary>Nº de líneas cuyo producto no tenía las 3 dimensiones cargadas.</summary>
    public required int ItemsWithoutDimensions { get; init; }
}

/// <summary>
/// Heurística única de agregación del carrito en un bulto.
/// Clase pura: sin EF, sin HTTP, sin estado. Toda la heurística vive aquí para que
/// calibrarla con datos reales de facturación sea cambiar parámetros de configuración.
/// </summary>
public static class ShippingPackageBuilder
{
    public static ShippingPackage Build(
        IEnumerable<ShoppingCartItem> items,
        string originDaneCode,
        string destinationDaneCode,
        ShippingDefaultsOptions defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);

        long totalGrams = 0;
        decimal totalVolumeCm3 = 0m;
        decimal maxLength = 0m;
        decimal maxWidth = 0m;
        decimal maxHeight = 0m;
        decimal declaredValue = 0m;
        int itemsWithoutWeight = 0;
        int itemsWithoutDimensions = 0;

        foreach (var item in items ?? Enumerable.Empty<ShoppingCartItem>())
        {
            if (item == null) continue;

            var quantity = item.Quantity;
            if (quantity <= 0) continue;

            var product = item.Product;

            // 1. Peso: gramos por unidad, con default de configuración si falta.
            var weightGrams = Positive(product?.ShippingWeightGrams);
            if (weightGrams == null)
            {
                itemsWithoutWeight++;
                weightGrams = defaults.WeightGrams > 0 ? defaults.WeightGrams : 500;
            }
            totalGrams += (long)quantity * weightGrams.Value;

            // 2. Dimensiones por unidad, con defaults donde falten.
            var length = Positive(product?.LengthCm);
            var width = Positive(product?.WidthCm);
            var height = Positive(product?.HeightCm);
            if (length == null || width == null || height == null) itemsWithoutDimensions++;

            var l = length ?? defaults.LengthCm;
            var w = width ?? defaults.WidthCm;
            var h = height ?? defaults.HeightCm;

            totalVolumeCm3 += quantity * l * w * h;

            if (l > maxLength) maxLength = l;
            if (w > maxWidth) maxWidth = w;
            if (h > maxHeight) maxHeight = h;

            // 3. Valor declarado = subtotal del carrito.
            declaredValue += quantity * (product?.Price ?? 0m);
        }

        var rawWeightKg = totalGrams / 1000m;
        var maxWeight = defaults.MaxPackageWeightKg > 0 ? defaults.MaxPackageWeightKg : 30m;
        var exceedsMaxWeight = rawWeightKg > maxWeight;

        // Mínimo facturable: todas las transportadoras nacionales cobran mínimo 1 kg.
        var minBillable = defaults.MinBillableWeightKg > 0 ? defaults.MinBillableWeightKg : 1m;
        var weightKg = Math.Max(rawWeightKg, minBillable);

        // 4. Holgura de empaque (cartón, relleno, aire).
        var packingFactor = defaults.PackingFactor > 0 ? defaults.PackingFactor : 1m;
        var boxedVolume = totalVolumeCm3 * packingFactor;

        // 5. "Cubo ajustado": refleja cómo se empaca de verdad mucho mejor que apilar
        //    los ítems en columna (que sobreestima el peso volumétrico con la cantidad).
        var side = CubeRoot(boxedVolume);

        var packageLength = Math.Max(Math.Ceiling(Math.Max(side, maxLength)), Math.Ceiling(defaults.MinBoxLengthCm));
        var packageWidth = Math.Max(Math.Ceiling(Math.Max(side, maxWidth)), Math.Ceiling(defaults.MinBoxWidthCm));

        var baseArea = packageLength * packageWidth;
        var derivedHeight = baseArea > 0m ? boxedVolume / baseArea : 0m;

        // La caja tiene que caber el ítem más alto, además del volumen agregado.
        var packageHeight = Math.Max(Math.Ceiling(Math.Max(derivedHeight, maxHeight)), Math.Ceiling(defaults.MinBoxHeightCm));

        var request = new ShippingQuoteRequest
        {
            OriginDaneCode = originDaneCode ?? string.Empty,
            DestinationDaneCode = destinationDaneCode ?? string.Empty,
            WeightKg = decimal.Round(weightKg, 3, MidpointRounding.AwayFromZero),
            LengthCm = packageLength,
            WidthCm = packageWidth,
            HeightCm = packageHeight,
            DeclaredValue = decimal.Round(declaredValue, 2, MidpointRounding.AwayFromZero)
        };

        return new ShippingPackage
        {
            Request = request,
            RawWeightKg = decimal.Round(rawWeightKg, 3, MidpointRounding.AwayFromZero),
            ExceedsMaxWeight = exceedsMaxWeight,
            ItemsWithoutWeight = itemsWithoutWeight,
            ItemsWithoutDimensions = itemsWithoutDimensions
        };
    }

    private static int? Positive(int? value) => value.HasValue && value.Value > 0 ? value : null;

    private static decimal? Positive(decimal? value) => value.HasValue && value.Value > 0m ? value : null;

    private static decimal CubeRoot(decimal volume)
    {
        if (volume <= 0m) return 0m;
        return (decimal)Math.Cbrt((double)volume);
    }
}

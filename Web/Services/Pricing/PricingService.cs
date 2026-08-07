using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Web.Data;
using Web.Models;
using Web.Models.Enums;

namespace Web.Services.Pricing;

/// <summary>
/// Capa de datos del pricing: lee la configuración de márgenes (cacheada), aplica los
/// cambios de precio sobre un <see cref="Product"/> y deja la entrada correspondiente en
/// <see cref="PriceChangeLog"/>.
///
/// ⚠️ Ninguno de los métodos llama a <c>SaveChangesAsync</c>: mutan las entidades
/// trackeadas y encolan el log para que el controlador guarde TODO junto (y, si hace
/// falta, dentro de su propia transacción). El cálculo puro vive en
/// <see cref="PricingCalculator"/>; aquí solo hay orquestación.
/// </summary>
public sealed class PricingService
{
    private const string SettingsCacheKey = "pricing:settings";
    private static readonly TimeSpan SettingsCacheTtl = TimeSpan.FromMinutes(5);

    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _cache;

    public PricingService(ApplicationDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    // ----------------------------------------------------------- Configuración

    /// <summary>
    /// Configuración de márgenes vigente. Degradación segura: si la fila única todavía no
    /// existe (base sin sembrar), devuelve los valores por defecto del PO (30/15/50) en
    /// memoria en vez de reventar el catálogo.
    /// </summary>
    public async Task<PricingSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(SettingsCacheKey, out PricingSettings? cached) && cached is not null)
        {
            return cached;
        }

        var settings = await _context.PricingSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(ct)
            ?? new PricingSettings
            {
                PricingSettingsID = PricingSettings.SingletonId,
                TargetMarginPercent = 30m,
                MinimumMarginPercent = 15m,
                RoundingStep = PricingCalculator.DefaultRoundingStep,
                Status = true,
                CreatedBy = "SYSTEM",
                CreatedOn = DateTime.Now
            };

        _cache.Set(SettingsCacheKey, settings, SettingsCacheTtl);
        return settings;
    }

    /// <summary>Invalida la caché tras editar la configuración (la llama el controlador).</summary>
    public void InvalidateSettingsCache() => _cache.Remove(SettingsCacheKey);

    // ------------------------------------------------------------- Operaciones

    /// <summary>
    /// El PROVEEDOR cambia su costo. Regla del PO: el PVP NO se mueve. Si el margen
    /// resultante cae bajo el mínimo se enciende <see cref="Product.MarginAlert"/>, pero
    /// el producto SIGUE vendiéndose (no se bloquea el catálogo por un tema de margen).
    /// </summary>
    public async Task<PriceChangeResult> ApplySupplierPriceAsync(
        Product product,
        decimal newSupplierPrice,
        string changedBy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(product);

        var settings = await GetSettingsAsync(ct);

        var oldSupplierPrice = product.SupplierPrice;
        var before = PricingCalculator.CalculateMargin(product.Price, oldSupplierPrice);
        var after = PricingCalculator.CalculateMargin(product.Price, newSupplierPrice);

        var changed = oldSupplierPrice != newSupplierPrice;

        product.SupplierPrice = newSupplierPrice;
        product.MarginAlert = PricingCalculator.IsBelowMinimum(
            product.Price, newSupplierPrice, settings.MinimumMarginPercent);

        if (changed)
        {
            AddLog(product.ProductID, PriceChangeType.SupplierPrice,
                oldSupplierPrice, newSupplierPrice, before, after, changedBy, reason: null);
        }

        return new PriceChangeResult(changed, before, after, product.MarginAlert);
    }

    /// <summary>
    /// BEAN fija el PVP. El llamador ya validó el permiso <c>Pricing/Update</c>; aquí solo
    /// se aplica y se registra. <paramref name="reason"/> es OBLIGATORIO cuando el precio
    /// queda bajo el margen mínimo (la valida el controlador, que es quien puede devolver
    /// el formulario con el error).
    /// </summary>
    public async Task<PriceChangeResult> ApplySalePriceAsync(
        Product product,
        decimal newPrice,
        string changedBy,
        string? reason,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(product);

        var settings = await GetSettingsAsync(ct);

        var oldPrice = product.Price;
        var before = PricingCalculator.CalculateMargin(oldPrice, product.SupplierPrice);
        var after = PricingCalculator.CalculateMargin(newPrice, product.SupplierPrice);

        var changed = oldPrice != newPrice;

        product.Price = newPrice;
        product.PriceSetAt = DateTime.Now;
        product.PriceSetBy = changedBy;
        product.MarginAlert = PricingCalculator.IsBelowMinimum(
            newPrice, product.SupplierPrice, settings.MinimumMarginPercent);

        if (changed)
        {
            AddLog(product.ProductID, PriceChangeType.SalePrice,
                oldPrice, newPrice, before, after, changedBy, reason);
        }

        return new PriceChangeResult(changed, before, after, product.MarginAlert);
    }

    /// <summary>
    /// Recalcula la alerta sin registrar nada en el histórico. Se usa cuando cambia la
    /// configuración de márgenes y hay que repasar el catálogo.
    /// </summary>
    public async Task<bool> RefreshMarginAlertAsync(Product product, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(product);

        var settings = await GetSettingsAsync(ct);

        product.MarginAlert = PricingCalculator.IsBelowMinimum(
            product.Price, product.SupplierPrice, settings.MinimumMarginPercent);

        return product.MarginAlert;
    }

    // ---------------------------------------------------------------- Helpers

    private void AddLog(
        Guid productId,
        PriceChangeType type,
        decimal oldValue,
        decimal newValue,
        PricingMargin before,
        PricingMargin after,
        string changedBy,
        string? reason)
    {
        _context.PriceChangeLogs.Add(new PriceChangeLog
        {
            PriceChangeLogID = Guid.NewGuid(),
            ProductID = productId,
            ChangeType = type,
            OldValue = oldValue,
            NewValue = newValue,
            MarginBefore = before.Percent,
            MarginAfter = after.Percent,
            ChangedBy = string.IsNullOrWhiteSpace(changedBy) ? "SYSTEM" : changedBy,
            ChangedOn = DateTime.Now,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            NotifiedProvider = false
        });
    }
}

/// <summary>Qué pasó al aplicar un cambio de precio.</summary>
/// <param name="Changed">false si el valor enviado era el mismo que ya tenía (no se registra log).</param>
/// <param name="MarginBefore">Margen antes del cambio.</param>
/// <param name="MarginAfter">Margen después del cambio.</param>
/// <param name="MarginAlert">Estado final de <see cref="Product.MarginAlert"/>.</param>
public readonly record struct PriceChangeResult(
    bool Changed,
    PricingMargin MarginBefore,
    PricingMargin MarginAfter,
    bool MarginAlert);

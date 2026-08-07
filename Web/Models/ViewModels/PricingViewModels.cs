using System.ComponentModel.DataAnnotations;
using Web.Models.Enums;
using Web.Services.Pricing;

namespace Web.Models.ViewModels;

/// <summary>Una fila de la bandeja de precios (un lote con su costo, su PVP y su margen).</summary>
public class PricingRowViewModel
{
    public Guid ProductID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ProviderName { get; set; }
    public string? Lot { get; set; }
    public ProductStatus ProductStatus { get; set; }
    public decimal SupplierPrice { get; set; }
    public decimal Price { get; set; }
    public bool MarginAlert { get; set; }
    public DateTime? PriceSetAt { get; set; }
    public string? PriceSetBy { get; set; }

    public PricingMargin Margin => PricingCalculator.CalculateMargin(Price, SupplierPrice);
}

/// <summary>Bandeja de precios: "Márgenes por revisar" + catálogo completo.</summary>
public class PricingIndexViewModel
{
    public const int PageSizeValue = 25;

    /// <summary>Pestaña activa: <c>alertas</c> (por defecto) o <c>todos</c>.</summary>
    public string Tab { get; set; } = "alertas";

    public string? Query { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = PageSizeValue;
    public int TotalCount { get; set; }

    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public int CountAlerts { get; set; }
    public int CountAll { get; set; }

    public decimal TargetMarginPercent { get; set; }
    public decimal MinimumMarginPercent { get; set; }
    public decimal RoundingStep { get; set; }

    public List<PricingRowViewModel> Rows { get; set; } = new();

    public decimal Suggested(PricingRowViewModel row) =>
        PricingCalculator.SuggestPrice(row.SupplierPrice, TargetMarginPercent, RoundingStep);
}

/// <summary>Histórico de cambios de precio de un lote.</summary>
public class PricingHistoryViewModel
{
    public Guid ProductID { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ProviderName { get; set; }
    public decimal SupplierPrice { get; set; }
    public decimal Price { get; set; }
    public bool MarginAlert { get; set; }

    public decimal TargetMarginPercent { get; set; }
    public decimal MinimumMarginPercent { get; set; }
    public decimal RoundingStep { get; set; }

    public PricingMargin Margin => PricingCalculator.CalculateMargin(Price, SupplierPrice);

    public decimal Suggested => PricingCalculator.SuggestPrice(SupplierPrice, TargetMarginPercent, RoundingStep);

    public List<PriceChangeLog> Changes { get; set; } = new();
}

/// <summary>Formulario de configuración de márgenes (solo SuperAdmin).</summary>
public class PricingSettingsViewModel
{
    [Display(Name = "Margen objetivo (%)")]
    [Range(0, 99.99, ErrorMessage = "El margen objetivo debe estar entre 0 y 99,99 %")]
    public decimal TargetMarginPercent { get; set; }

    [Display(Name = "Margen mínimo (%)")]
    [Range(0, 99.99, ErrorMessage = "El margen mínimo debe estar entre 0 y 99,99 %")]
    public decimal MinimumMarginPercent { get; set; }

    [Display(Name = "Redondeo del precio sugerido (COP)")]
    [Range(1, 100000, ErrorMessage = "El redondeo debe estar entre 1 y 100.000")]
    public decimal RoundingStep { get; set; }

    /// <summary>Cuántos lotes quedaron marcados tras recalcular. Se llena al guardar.</summary>
    public int? RecalculatedAlerts { get; set; }
}

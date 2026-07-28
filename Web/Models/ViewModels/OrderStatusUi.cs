using Web.Models.Enums;

namespace Web.Models.ViewModels;

/// <summary>
/// Traducción de OrderStatus a etiqueta en español + clase de badge del design system
/// (variantes ya existentes en bean-theme.css: draft / pending / transit / ship / active / rejected).
/// </summary>
public static class OrderStatusUi
{
    public static (string Label, string CssClass) For(OrderStatus status) => status switch
    {
        OrderStatus.Pending => ("Pendiente de pago", "bean-status draft"),
        OrderStatus.Confirmed => ("Confirmado", "bean-status pending"),
        OrderStatus.Processing => ("En preparación", "bean-status transit"),
        OrderStatus.Shipped => ("Enviado", "bean-status ship"),
        OrderStatus.Delivered => ("Entregado", "bean-status active"),
        OrderStatus.Cancelled => ("Cancelado", "bean-status rejected"),
        _ => (status.ToString(), "bean-status draft")
    };
}

namespace Web.Models.Enums;

/// <summary>
/// Qué precio cambió en una entrada de <see cref="Web.Models.PriceChangeLog"/>.
/// Son dos dueños distintos: el COSTO lo mueve el proveedor, el PVP lo mueve Bean.
/// </summary>
public enum PriceChangeType
{
    /// <summary>Costo del proveedor (<c>Product.SupplierPrice</c>). Lo edita el proveedor.</summary>
    SupplierPrice = 0,

    /// <summary>Precio de venta al público (<c>Product.Price</c>). Solo con <c>Pricing/Update</c>.</summary>
    SalePrice = 1
}

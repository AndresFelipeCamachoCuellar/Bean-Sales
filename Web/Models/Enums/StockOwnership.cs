namespace Web.Models.Enums;

/// <summary>
/// De quién es el café que está físicamente en la bodega de Bean.
/// Se CONGELA en el momento de la recepción según el acuerdo vigente ese día (E3):
/// cambiar el acuerdo después NO reescribe stock ya recibido.
/// </summary>
public enum StockOwnership
{
    /// <summary>Compra en firme: Bean ya pagó el lote y es suyo.</summary>
    BeanOwned = 0,

    /// <summary>Consignación: el lote sigue siendo del proveedor hasta que se venda.</summary>
    Consignment = 1
}

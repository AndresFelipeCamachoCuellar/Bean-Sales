using Web.Models.Enums;

namespace Web.Models.ViewModels;

/// <summary>
/// Una celda de la matriz producto × bodega. Es un DTO plano: toda la aritmética ya viene
/// resuelta desde el controlador para no meter lógica en la vista.
/// </summary>
public class InventoryCellViewModel
{
    /// <summary>false = ese producto nunca ha tenido saldo en esa bodega (no es lo mismo que 0).</summary>
    public bool Exists { get; set; }

    public int QuantityOnHand { get; set; }
    public int QuantityReserved { get; set; }
    public int? ReorderPoint { get; set; }
    public StockOwnership Ownership { get; set; } = StockOwnership.BeanOwned;

    public int QuantityAvailable => QuantityOnHand - QuantityReserved;

    /// <summary>
    /// Estado visual de la celda (handoff vista 11):
    /// <c>none</c> no asignado · <c>empty</c> agotado · <c>low</c> bajo el punto de reorden · <c>ok</c>.
    /// </summary>
    public string State
    {
        get
        {
            if (!Exists) return "none";
            if (QuantityAvailable <= 0) return "empty";
            if (ReorderPoint.HasValue && QuantityAvailable <= ReorderPoint.Value) return "low";
            return "ok";
        }
    }

    /// <summary>Texto del tooltip: en mano y reservado (lo pidió el PO explícitamente).</summary>
    public string Tooltip => Exists
        ? $"En mano: {QuantityOnHand} · Reservado: {QuantityReserved} · Disponible: {QuantityAvailable}"
          + (ReorderPoint.HasValue ? $" · Punto de reorden: {ReorderPoint.Value}" : " · Sin punto de reorden")
        : "Este lote nunca ha tenido saldo en esta bodega.";
}

/// <summary>Una columna de la matriz: una bodega activa, con su total.</summary>
public class InventoryColumnViewModel
{
    public Guid WarehouseID { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;

    /// <summary>Total de la bodega sobre el conjunto FILTRADO de productos (no solo la página).</summary>
    public int TotalAvailable { get; set; }
    public int TotalOnHand { get; set; }
    public int TotalReserved { get; set; }
}

/// <summary>Una fila de la matriz: un lote, con una celda por bodega y su total.</summary>
public class InventoryRowViewModel
{
    public Guid ProductID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Lot { get; set; }
    public string? ProviderName { get; set; }
    public ProductStatus ProductStatus { get; set; }

    /// <summary>Total denormalizado de <c>Product.Stock</c> (el que consume el catálogo).</summary>
    public int ProductStock { get; set; }

    /// <summary>Celdas indexadas por bodega. Solo existen las bodegas visibles.</summary>
    public Dictionary<Guid, InventoryCellViewModel> Cells { get; set; } = new();

    public int TotalOnHand { get; set; }
    public int TotalReserved { get; set; }
    public int TotalAvailable => TotalOnHand - TotalReserved;

    /// <summary>true si alguna bodega está bajo su punto de reorden.</summary>
    public bool HasLowStock => Cells.Values.Any(c => c.State == "low");

    public InventoryCellViewModel CellFor(Guid warehouseId) =>
        Cells.TryGetValue(warehouseId, out var cell) ? cell : new InventoryCellViewModel();
}

/// <summary>
/// Matriz de disponibilidad producto × bodega (HU-1.1). Filas = lote, columnas = bodega,
/// celda = disponible.
/// </summary>
public class InventoryMatrixViewModel
{
    public const int PageSizeValue = 25;

    // ---- Filtros ----
    public Guid? WarehouseID { get; set; }
    public Guid? ProviderID { get; set; }
    public ProductStatus? Estado { get; set; }
    public bool SoloStockBajo { get; set; }
    public string? Query { get; set; }

    // ---- Paginación ----
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = PageSizeValue;
    public int TotalCount { get; set; }
    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrev => Page > 1;
    public bool HasNext => Page < TotalPages;

    // ---- Datos ----
    public List<InventoryColumnViewModel> Columns { get; set; } = new();
    public List<InventoryRowViewModel> Rows { get; set; } = new();

    /// <summary>Opciones de los selectores de filtro.</summary>
    public List<InventoryFilterOption> WarehouseOptions { get; set; } = new();
    public List<InventoryFilterOption> ProviderOptions { get; set; } = new();

    /// <summary>Lotes bajo el punto de reorden en TODO el inventario (no solo la página).</summary>
    public int LowStockCount { get; set; }

    /// <summary>
    /// true solo cuando NO existe ninguna bodega operativa en el sistema (estado vacío con
    /// el enlace para crear la primera). Ojo: no se deriva de <see cref="Columns"/>, porque
    /// filtrar por una bodega inexistente también dejaría la matriz sin columnas y el
    /// mensaje sería mentira.
    /// </summary>
    public bool NoWarehouses => WarehouseOptions.Count == 0;

    public int GrandTotalAvailable => Columns.Sum(c => c.TotalAvailable);
}

public class InventoryFilterOption
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
}

/// <summary>Saldo de un lote en una bodega concreta (bloque superior de <c>Details</c>).</summary>
public class InventoryBalanceRowViewModel
{
    public Guid WarehouseID { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public bool WarehouseIsActive { get; set; }

    public int QuantityOnHand { get; set; }
    public int QuantityReserved { get; set; }
    public int QuantityAvailable => QuantityOnHand - QuantityReserved;
    public int? ReorderPoint { get; set; }
    public StockOwnership Ownership { get; set; } = StockOwnership.BeanOwned;

    /// <summary>false = la bodega existe pero este lote nunca ha tenido saldo ahí.</summary>
    public bool HasStockItem { get; set; }

    public bool IsLow => HasStockItem && ReorderPoint.HasValue
                         && QuantityAvailable > 0 && QuantityAvailable <= ReorderPoint.Value;
}

/// <summary>Una línea del libro mayor, ya aplanada para la vista.</summary>
public class InventoryMovementRowViewModel
{
    public Guid StockMovementID { get; set; }
    public DateTime CreatedOn { get; set; }
    public StockMovementType MovementType { get; set; }
    public int Quantity { get; set; }
    public string WarehouseCode { get; set; } = string.Empty;
    public string? ReferenceType { get; set; }
    public Guid? ReferenceID { get; set; }
    public string? Reason { get; set; }
    public decimal? UnitCost { get; set; }
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Id corto y legible del documento de origen (#A1B2C3D4).</summary>
    public string? ReferenceShortId =>
        ReferenceID.HasValue ? ReferenceID.Value.ToString("N").Substring(0, 8).ToUpperInvariant() : null;
}

/// <summary>
/// Pantalla de auditoría de un lote (HU-1.2): saldo por bodega + libro mayor paginado.
/// Si un número no cuadra, aquí se ve por qué.
/// </summary>
public class InventoryDetailsViewModel
{
    public const int PageSizeValue = 25;

    public Guid ProductID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Lot { get; set; }
    public string? ProviderName { get; set; }
    public ProductStatus ProductStatus { get; set; }

    /// <summary>Total denormalizado que consume el catálogo.</summary>
    public int ProductStock { get; set; }

    public List<InventoryBalanceRowViewModel> Balances { get; set; } = new();
    public List<InventoryMovementRowViewModel> Movements { get; set; } = new();

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = PageSizeValue;
    public int TotalMovements { get; set; }
    public int TotalPages => TotalMovements == 0 ? 1 : (int)Math.Ceiling(TotalMovements / (double)PageSize);
    public bool HasPrev => Page > 1;
    public bool HasNext => Page < TotalPages;

    /// <summary>Bodegas activas disponibles para el formulario de ajuste.</summary>
    public List<InventoryFilterOption> WarehouseOptions { get; set; } = new();

    public bool CanAdjust { get; set; }

    public int TotalOnHand => Balances.Sum(b => b.QuantityOnHand);
    public int TotalReserved => Balances.Sum(b => b.QuantityReserved);
    public int TotalAvailable => TotalOnHand - TotalReserved;

    /// <summary>
    /// Suma firmada del libro mayor de ESTA página. No es la invariante completa (el libro
    /// está paginado), solo un apoyo visual.
    /// </summary>
    public int PageMovementBalance => Movements.Sum(m => m.Quantity);
}

/// <summary>Etiquetas en español de los tipos de movimiento y su tinte en la UI.</summary>
public static class StockMovementUi
{
    public static string Label(StockMovementType type) => type switch
    {
        StockMovementType.Reception => "Recepción",
        StockMovementType.Sale => "Venta",
        StockMovementType.SaleCancelled => "Venta cancelada",
        StockMovementType.CustomerReturn => "Devolución de cliente",
        StockMovementType.Adjustment => "Ajuste manual",
        StockMovementType.TransferOut => "Traslado (salida)",
        StockMovementType.TransferIn => "Traslado (entrada)",
        StockMovementType.Loss => "Merma / pérdida",
        StockMovementType.ReturnToProvider => "Devolución al proveedor",
        _ => type.ToString()
    };

    /// <summary>Clase del badge <c>bean-status</c> según si el asiento suma o resta.</summary>
    public static string CssClass(StockMovementType type) => type switch
    {
        StockMovementType.Reception => "bean-status active",
        StockMovementType.SaleCancelled => "bean-status active",
        StockMovementType.CustomerReturn => "bean-status active",
        StockMovementType.TransferIn => "bean-status active",
        StockMovementType.Sale => "bean-status ship",
        StockMovementType.TransferOut => "bean-status ship",
        StockMovementType.Loss => "bean-status rejected",
        StockMovementType.ReturnToProvider => "bean-status rejected",
        StockMovementType.Adjustment => "bean-status pending",
        _ => "bean-status draft"
    };

    public static string OwnershipLabel(StockOwnership ownership) => ownership switch
    {
        StockOwnership.Consignment => "Consignación",
        _ => "Propiedad de Bean"
    };
}

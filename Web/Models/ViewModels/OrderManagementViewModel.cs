using Web.Models.Enums;

namespace Web.Models.ViewModels;

/// <summary>Bandeja de pedidos del admin (listado + filtros + contadores + paginación).</summary>
public class OrderManagementViewModel
{
    public List<OrderRowViewModel> Orders { get; set; } = new();

    // ---- Filtros activos (se conservan en tabs y paginación) ----
    public string? Query { get; set; }

    /// <summary>
    /// Filtro de pestaña. Confirmed representa el grupo "Nuevos" (Pending + Confirmed).
    /// null = todos.
    /// </summary>
    public OrderStatus? Estado { get; set; }

    // ---- Paginación ----
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public int TotalCount { get; set; }

    public int TotalPages => PageSize <= 0 || TotalCount <= 0
        ? 1
        : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPrev => Page > 1;
    public bool HasNext => Page < TotalPages;

    // ---- Contadores por pestaña (una sola query agrupada) ----
    public int CountAll { get; set; }
    public int CountNew { get; set; }          // Pending + Confirmed
    public int CountProcessing { get; set; }
    public int CountShipped { get; set; }
    public int CountDelivered { get; set; }
    public int CountCancelled { get; set; }
}

/// <summary>Fila del listado (proyección plana, sin Include).</summary>
public class OrderRowViewModel
{
    public Guid OrderID { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public DateTime OrderDate { get; set; }
    public OrderStatus Status { get; set; }
    public decimal TotalAmount { get; set; }
    public string CurrencyCode { get; set; } = "COP";

    /// <summary>Unidades totales del pedido.</summary>
    public int Bags { get; set; }

    /// <summary>Número de líneas (lotes distintos).</summary>
    public int Lines { get; set; }

    public string ShortId => OrderID.ToString("N").Substring(0, 8).ToUpperInvariant();
    public string FullName => $"{FirstName} {LastName}".Trim();
}

/// <summary>Detalle del pedido + transiciones disponibles según el workflow.</summary>
public class OrderDetailViewModel
{
    public Order Order { get; set; } = null!;

    /// <summary>Estados a los que se puede avanzar (según OrderWorkflow).</summary>
    public IReadOnlyList<OrderStatus> NextStates { get; set; } = Array.Empty<OrderStatus>();

    /// <summary>El workflow permite cancelar (independiente del permiso del usuario).</summary>
    public bool CanCancel { get; set; }

    /// <summary>Al cancelar se reintegraría el inventario.</summary>
    public bool WillRestock { get; set; }

    /// <summary>Unidades que se devolverían al inventario si se cancela.</summary>
    public int UnitsToRestock { get; set; }

    public bool IsTerminal => NextStates.Count == 0;
    public string ShortId => Order.OrderID.ToString("N").Substring(0, 8).ToUpperInvariant();
}

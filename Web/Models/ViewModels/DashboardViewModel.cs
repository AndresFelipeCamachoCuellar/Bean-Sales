namespace Web.Models.ViewModels;

// Datos del Dashboard del admin (handoff vista 9). Todo son KPIs y listas
// derivados de datos REALES (Orders / OrderItems / Products / Providers).
public class DashboardViewModel
{
    // Cabecera
    public string Greeting { get; set; } = string.Empty;   // "Buenos días, Andrés"
    public string TodayLabel { get; set; } = string.Empty; // "viernes 17 de julio · resumen de la operación"

    // 4 KPI cards
    public List<DashboardKpi> Kpis { get; set; } = new();

    // Gráfica "Ventas por semana" (barras CSS S1..S8)
    public List<WeeklySalesBar> WeeklySales { get; set; } = new();

    // Fila de totales bajo la gráfica (ya formateados)
    public string MonthSalesLabel { get; set; } = "$0";
    public string MonthBagsLabel { get; set; } = "0";
    public string MonthOrdersLabel { get; set; } = "0";

    // Card "Por aprobar" (hasta 5 productos PendingApproval)
    public int PendingApprovalsTotal { get; set; }
    public List<PendingProductRow> PendingProducts { get; set; } = new();
    public string? OldestPendingAge { get; set; } // "3 días" (antigüedad de la cola)

    // "Actividad reciente"
    public List<ActivityItem> RecentActivity { get; set; } = new();
}

public class DashboardKpi
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty; // ya formateado
    public bool HasTrend { get; set; }
    public bool TrendUp { get; set; }
    public string? TrendLabel { get; set; } // "12% vs mes anterior"
}

public class WeeklySalesBar
{
    public string Label { get; set; } = string.Empty; // S1..S8
    public decimal Total { get; set; }
    public int HeightPercent { get; set; } // 8..100, relativo al máximo
    public bool IsCurrent { get; set; }
    public string ValueLabel { get; set; } = string.Empty; // tooltip formateado
}

public class PendingProductRow
{
    public string Name { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string AgeLabel { get; set; } = string.Empty; // "hace 3 días"
}

// Tipos de actividad -> icono/tinte (handoff): approval | order | alert | user
public class ActivityItem
{
    public string Type { get; set; } = "order";
    public string Text { get; set; } = string.Empty;
    public string TimeLabel { get; set; } = string.Empty; // "hace 2 h"
}

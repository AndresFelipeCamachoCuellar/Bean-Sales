using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Web.Constants;
using Web.Data;
using Web.Models.Enums;
using Web.Models.ViewModels;

namespace Web.Controllers;

// Dashboard del admin (handoff vista 9). Solo SuperAdmin (mismo patrón que ProvidersController).
[Authorize(Roles = Roles.SuperAdmin)]
public class DashboardController : Controller
{
    private readonly ApplicationDbContext _context;
    private static readonly CultureInfo EsCo = CultureInfo.GetCultureInfo("es-CO");

    public DashboardController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var now = DateTime.Now;
        var today = now.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var prevMonthStart = monthStart.AddMonths(-1);

        // ---- KPIs (datos reales) ----
        // Ventas del mes actual (SumAsync sobre decimal no-nullable => 0 si vacío).
        // ⚠️ Solo cuentan los pedidos PAGADOS: desde la integración con Wompi existen
        //    pedidos en Pending (stock reservado) que todavía no son una venta.
        var salesThisMonth = await _context.Orders
            .Where(o => o.OrderDate >= monthStart && o.PaymentStatus == PaymentStatus.Approved)
            .SumAsync(o => o.TotalAmount);

        var salesPrevMonth = await _context.Orders
            .Where(o => o.OrderDate >= prevMonthStart && o.OrderDate < monthStart
                        && o.PaymentStatus == PaymentStatus.Approved)
            .SumAsync(o => o.TotalAmount);

        // Nº de pedidos (total histórico) y del mes/mes anterior (para tendencia real).
        var ordersTotal = await _context.Orders.CountAsync();
        var ordersThisMonth = await _context.Orders.CountAsync(o => o.OrderDate >= monthStart);
        var ordersPrevMonth = await _context.Orders
            .CountAsync(o => o.OrderDate >= prevMonthStart && o.OrderDate < monthStart);

        // Productos pendientes de aprobación (mismo criterio que el badge del sidebar).
        var pendingApprovals = await _context.Products
            .CountAsync(p => p.ProductStatus == ProductStatus.PendingApproval && p.Status);

        // 4º KPI real: pedidos por despachar (confirmados o en preparación).
        var ordersToShip = await _context.Orders
            .CountAsync(o => o.OrderStatus == OrderStatus.Confirmed || o.OrderStatus == OrderStatus.Processing);

        // Bolsas vendidas este mes (suma de cantidades de líneas de pedidos del mes).
        var bagsThisMonth = await _context.OrderItems
            .Where(oi => oi.Order!.OrderDate >= monthStart
                         && oi.Order.PaymentStatus == PaymentStatus.Approved)
            .SumAsync(oi => (int?)oi.Quantity) ?? 0;

        var vm = new DashboardViewModel
        {
            Greeting = $"{GreetingPrefix(now)}, {(User.Identity?.Name ?? "admin")}",
            TodayLabel = $"{now.ToString("dddd d 'de' MMMM", EsCo)} · resumen de la operación",
            MonthSalesLabel = salesThisMonth.ToString("C0", EsCo),
            MonthBagsLabel = bagsThisMonth.ToString("N0", EsCo),
            MonthOrdersLabel = ordersThisMonth.ToString("N0", EsCo),
            PendingApprovalsTotal = pendingApprovals
        };

        vm.Kpis.Add(BuildKpi("Ventas del mes", salesThisMonth.ToString("C0", EsCo), salesThisMonth, salesPrevMonth));
        vm.Kpis.Add(BuildKpi("Pedidos (mes)", ordersThisMonth.ToString("N0", EsCo), ordersThisMonth, ordersPrevMonth,
            trendSuffix: "vs mes anterior"));
        vm.Kpis.Add(new DashboardKpi
        {
            Label = "Por aprobar",
            Value = pendingApprovals.ToString("N0", EsCo),
            HasTrend = false
        });
        vm.Kpis.Add(new DashboardKpi
        {
            Label = "Por despachar",
            Value = ordersToShip.ToString("N0", EsCo),
            HasTrend = false
        });

        // 5º KPI (E2): lotes cuyo margen quedó bajo el mínimo o que aún no tienen PVP.
        // Es la bandeja "Márgenes por revisar" de /Pricing.
        var marginAlerts = await _context.Products.CountAsync(p => p.MarginAlert && p.Status);
        vm.Kpis.Add(new DashboardKpi
        {
            Label = "Márgenes por revisar",
            Value = marginAlerts.ToString("N0", EsCo),
            HasTrend = false
        });

        // ---- Gráfica "Ventas por semana": 8 semanas (7 previas + actual) ----
        // La agrupación por semana es difícil de traducir a SQL de forma portable,
        // así que se materializan las órdenes del rango y se agrupan EN MEMORIA
        // (volumen bajo: solo OrderDate + TotalAmount de ~8 semanas).
        int diffToMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var currentWeekStart = today.AddDays(-diffToMonday);
        var rangeStart = currentWeekStart.AddDays(-7 * 7); // inicio de la semana S1

        var ordersInRange = await _context.Orders
            .Where(o => o.OrderDate >= rangeStart && o.PaymentStatus == PaymentStatus.Approved)
            .Select(o => new { o.OrderDate, o.TotalAmount })
            .ToListAsync();

        for (int i = 0; i < 8; i++)
        {
            var wStart = currentWeekStart.AddDays(-7 * (7 - i));
            var wEnd = wStart.AddDays(7);
            var total = ordersInRange
                .Where(o => o.OrderDate >= wStart && o.OrderDate < wEnd)
                .Sum(o => o.TotalAmount);

            vm.WeeklySales.Add(new WeeklySalesBar
            {
                Label = $"S{i + 1}",
                Total = total,
                IsCurrent = i == 7,
                ValueLabel = total.ToString("C0", EsCo)
            });
        }

        var maxWeek = vm.WeeklySales.Count > 0 ? vm.WeeklySales.Max(b => b.Total) : 0m;
        foreach (var bar in vm.WeeklySales)
        {
            bar.HeightPercent = maxWeek > 0
                ? Math.Max(8, (int)Math.Round((double)(bar.Total / maxWeek) * 100))
                : 8; // altura mínima para que la barra sea visible aun sin ventas
        }

        // ---- Card "Por aprobar": hasta 5 productos pendientes ----
        var pendingList = await _context.Products
            .Where(p => p.ProductStatus == ProductStatus.PendingApproval && p.Status)
            .OrderBy(p => p.CreatedOn)
            .Take(5)
            .Select(p => new { p.Name, ProviderName = p.Provider!.Name, p.CreatedOn })
            .ToListAsync();

        vm.PendingProducts = pendingList
            .Select(p => new PendingProductRow
            {
                Name = p.Name,
                ProviderName = p.ProviderName ?? "Proveedor",
                AgeLabel = RelativeTime(p.CreatedOn, now)
            })
            .ToList();

        if (pendingList.Count > 0)
        {
            var oldest = pendingList.Min(p => p.CreatedOn);
            vm.OldestPendingAge = AgeSpan(oldest, now);
        }

        // ---- Actividad reciente: se combinan pedidos + cambios de producto + empresas ----
        var activityPool = new List<(DateTime When, ActivityItem Item)>();

        var recentOrders = await _context.Orders
            .Where(o => o.PaymentStatus == PaymentStatus.Approved)
            .OrderByDescending(o => o.OrderDate)
            .Take(5)
            .Select(o => new
            {
                o.OrderID,
                o.FirstName,
                o.LastName,
                o.ShippingCountry,
                o.TotalAmount,
                o.CurrencyCode,
                o.OrderDate,
                Bags = o.Items.Sum(i => (int?)i.Quantity) ?? 0
            })
            .ToListAsync();

        foreach (var o in recentOrders)
        {
            var shortId = o.OrderID.ToString("N").Substring(0, 6).ToUpperInvariant();
            var buyer = $"{o.FirstName} {o.LastName}".Trim();
            var dest = string.IsNullOrWhiteSpace(o.ShippingCountry) ? "" : $", {o.ShippingCountry}";
            activityPool.Add((o.OrderDate, new ActivityItem
            {
                Type = "order",
                Text = $"Pedido #{shortId} ({buyer}{dest}) — {o.Bags} bolsas por {o.TotalAmount.ToString("C0", EsCo)} {o.CurrencyCode}.",
                TimeLabel = RelativeTime(o.OrderDate, now)
            }));
        }

        var recentProducts = await _context.Products
            .Where(p => p.UpdatedOn != null && p.Status)
            .OrderByDescending(p => p.UpdatedOn)
            .Take(5)
            .Select(p => new { p.Name, ProviderName = p.Provider!.Name, p.ProductStatus, p.UpdatedOn })
            .ToListAsync();

        foreach (var p in recentProducts)
        {
            var when = p.UpdatedOn!.Value;
            var prov = p.ProviderName ?? "un proveedor";
            var (type, text) = p.ProductStatus switch
            {
                ProductStatus.Active =>
                    ("approval", $"“{p.Name}” de {prov} ya está publicado y disponible."),
                ProductStatus.ApprovedToShip =>
                    ("approval", $"Aprobaste “{p.Name}” de {prov} — pendiente de recibir en bodega."),
                ProductStatus.Rejected =>
                    ("alert", $"Rechazaste “{p.Name}” de {prov}."),
                ProductStatus.Shipped =>
                    ("order", $"“{p.Name}” de {prov} va en camino a la bodega."),
                _ => ("order", $"“{p.Name}” de {prov} cambió de estado.")
            };
            activityPool.Add((when, new ActivityItem { Type = type, Text = text, TimeLabel = RelativeTime(when, now) }));
        }

        var recentProviders = await _context.Providers
            .Where(pr => pr.Status)
            .OrderByDescending(pr => pr.CreatedOn)
            .Take(3)
            .Select(pr => new { pr.Name, pr.ApprovalStatus, pr.CreatedOn })
            .ToListAsync();

        foreach (var pr in recentProviders)
        {
            var text = pr.ApprovalStatus == Models.ApprovalStatus.Pending
                ? $"Nueva empresa registrada: “{pr.Name}” — espera verificación de proveedor."
                : $"Empresa “{pr.Name}” verificada como proveedor.";
            activityPool.Add((pr.CreatedOn, new ActivityItem
            {
                Type = "user",
                Text = text,
                TimeLabel = RelativeTime(pr.CreatedOn, now)
            }));
        }

        vm.RecentActivity = activityPool
            .OrderByDescending(a => a.When)
            .Take(6)
            .Select(a => a.Item)
            .ToList();

        return View(vm);
    }

    // ---- Helpers ----
    private static string GreetingPrefix(DateTime now) => now.Hour switch
    {
        < 12 => "Buenos días",
        < 19 => "Buenas tardes",
        _ => "Buenas noches"
    };

    private static DashboardKpi BuildKpi(string label, string value, decimal current, decimal previous,
        string trendSuffix = "vs mes anterior")
    {
        var kpi = new DashboardKpi { Label = label, Value = value };
        if (previous > 0)
        {
            var pct = (double)((current - previous) / previous) * 100.0;
            kpi.HasTrend = true;
            kpi.TrendUp = pct >= 0;
            kpi.TrendLabel = $"{Math.Abs(pct).ToString("0", EsCo)}% {trendSuffix}";
        }
        else if (current > 0)
        {
            // Sin base el mes anterior pero hay ventas/pedidos ahora: tendencia positiva.
            kpi.HasTrend = true;
            kpi.TrendUp = true;
            kpi.TrendLabel = "nuevo este mes";
        }
        return kpi;
    }

    private static string RelativeTime(DateTime when, DateTime now)
    {
        var span = now - when;
        if (span.TotalMinutes < 1) return "ahora";
        if (span.TotalMinutes < 60) return $"hace {(int)span.TotalMinutes} min";
        if (span.TotalHours < 24) return $"hace {(int)span.TotalHours} h";
        var days = (int)span.TotalDays;
        if (days == 1) return "ayer";
        if (days < 30) return $"hace {days} días";
        var months = days / 30;
        return months == 1 ? "hace 1 mes" : $"hace {months} meses";
    }

    private static string AgeSpan(DateTime when, DateTime now)
    {
        var days = (int)(now - when).TotalDays;
        if (days <= 0)
        {
            var hours = (int)(now - when).TotalHours;
            return hours <= 1 ? "menos de 1 hora" : $"{hours} horas";
        }
        return days == 1 ? "1 día" : $"{days} días";
    }
}

using Microsoft.EntityFrameworkCore;
using MyAccountingApp.Core.Data;
using MyAccountingApp.Core.Enums;

namespace MyAccountingApp.Core.Services;

/// <summary>One chart bucket (an hour, day or month) starting at <see cref="Start"/> (local time).</summary>
public record TrendPoint(DateTime Start, decimal Sales, decimal Purchases, decimal Profit);

/// <summary>Amount per product / category / contact. <see cref="Id"/> and <see cref="Name"/> are null for
/// "General" (no category) and walk-in (no contact); <see cref="Count"/> = quantity for products, orders for contacts.</summary>
public record NamedAmount(int? Id, string? Name, decimal Amount, decimal Count);

public record DashboardReport(
    DateTime FromLocal, DateTime ToLocal,
    SalesSummary Sales,
    decimal Purchases, int PurchaseCount,
    List<TrendPoint> Trend,
    List<NamedAmount> SalesByProduct, List<NamedAmount> PurchasesByProduct,
    List<NamedAmount> SalesByCategory,
    List<NamedAmount> SalesByCustomer, List<NamedAmount> PurchasesBySupplier);

/// <summary>
/// Dashboard figures from the ledger, so returns and cancellations are netted out:
/// sales side = Sale + SaleReturn (amount = Σ −qty × price), purchase side = Purchase + PurchaseReturn
/// (amount = Σ qty × price). Rows are bucketed by their local posting time.
/// </summary>
public class ReportService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>
    /// [from, to) in local time of the calendar period holding <paramref name="today"/>: Day = today, Week = Saturday to Friday,
    /// Month = from the 1st, Year = from 1 January. The range runs to the end of the period (later buckets are just empty).
    /// </summary>
    public static (DateTime From, DateTime To) RangeOf(ReportPeriod period, DateTime today)
    {
        var day = today.Date;
        switch (period)
        {
            case ReportPeriod.Day:
                return (day, day.AddDays(1));
            case ReportPeriod.Week:
                var saturday = day.AddDays(-(((int)day.DayOfWeek - (int)DayOfWeek.Saturday + 7) % 7));
                return (saturday, saturday.AddDays(7));
            case ReportPeriod.Month:
                var first = new DateTime(day.Year, day.Month, 1);
                return (first, first.AddMonths(1));
            default:
                var jan = new DateTime(day.Year, 1, 1);
                return (jan, jan.AddYears(1));
        }
    }

    /// <summary>Start of the bucket holding <paramref name="local"/>.</summary>
    public static DateTime BucketOf(ReportPeriod period, DateTime local) => period switch
    {
        ReportPeriod.Day => local.Date.AddHours(local.Hour),
        ReportPeriod.Year => new DateTime(local.Year, local.Month, 1),
        _ => local.Date,
    };

    private static DateTime NextBucket(ReportPeriod period, DateTime start) => period switch
    {
        ReportPeriod.Day => start.AddHours(1),
        ReportPeriod.Year => start.AddMonths(1),
        _ => start.AddDays(1),
    };

    public async Task<DashboardReport> GetDashboardAsync(ReportPeriod period, DateTime? nowLocal = null)
    {
        var (from, to) = RangeOf(period, nowLocal ?? DateTime.Now);
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();

        await using var db = await dbFactory.CreateDbContextAsync();

        // Aggregated client-side: decimals are TEXT in SQLite.
        var rows = await db.Ledger.AsNoTracking()
            .Where(l => l.CreatedAt >= fromUtc && l.CreatedAt < toUtc && l.DocumentType != OrderType.OpeningStock)
            .Select(l => new
            {
                l.ProductId, l.DocumentId, l.DocumentType, l.Quantity, l.UnitCost, l.UnitPrice, l.CreatedAt,
                l.Product!.Name, l.Product.CategoryId, CategoryName = l.Product.Category!.Name,
            })
            .ToListAsync();

        var orderIds = rows.Select(r => r.DocumentId).Distinct().ToList();
        var orders = await db.Orders.AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .Select(o => new { o.Id, o.OrderType, o.Status, o.ContactId, ContactName = o.Contact!.Name })
            .ToDictionaryAsync(o => o.Id);

        var sales = rows.Where(r => OrderService.IsSalesSide(r.DocumentType))
            .Select(r => new
            {
                r.ProductId, r.Name, r.CategoryId, r.CategoryName, r.DocumentId, r.CreatedAt,
                Quantity = -r.Quantity, Amount = -r.Quantity * r.UnitPrice, Cost = -r.Quantity * r.UnitCost,
            })
            .ToList();
        var purchases = rows.Where(r => !OrderService.IsSalesSide(r.DocumentType))
            .Select(r => new { r.ProductId, r.Name, r.DocumentId, r.CreatedAt, r.Quantity, Amount = r.Quantity * r.UnitPrice })
            .ToList();

        // Still-confirmed orders only: a cancelled one nets to zero, so it shouldn't count either.
        bool Counts(int orderId, OrderType type) =>
            orders.TryGetValue(orderId, out var o) && o.OrderType == type && o.Status == OrderStatus.Confirmed;

        var summary = new SalesSummary(
            sales.Sum(r => r.Amount),
            sales.Sum(r => r.Cost),
            sales.Select(r => r.DocumentId).Distinct().Count(id => Counts(id, OrderType.Sale)),
            sales.Sum(r => r.Quantity));

        var trend = new List<TrendPoint>();
        var salesByBucket = sales.ToLookup(r => BucketOf(period, Local(r.CreatedAt)));
        var purchasesByBucket = purchases.ToLookup(r => BucketOf(period, Local(r.CreatedAt)));
        for (var start = from; start < to; start = NextBucket(period, start))
        {
            var s = salesByBucket[start];
            trend.Add(new TrendPoint(start, s.Sum(r => r.Amount), purchasesByBucket[start].Sum(r => r.Amount),
                s.Sum(r => r.Amount - r.Cost)));
        }

        NamedAmount ByContact(IGrouping<int?, int> g, Func<int, decimal> amountOf) => new(
            g.Key, g.Key is null ? null : orders.Values.First(o => o.ContactId == g.Key).ContactName,
            g.Sum(amountOf), g.Count(id => orders[id].Status == OrderStatus.Confirmed && orders[id].OrderType
                is OrderType.Sale or OrderType.Purchase));

        var saleAmountByOrder = sales.GroupBy(r => r.DocumentId).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
        var purchaseAmountByOrder = purchases.GroupBy(r => r.DocumentId).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));

        return new DashboardReport(
            from, to, summary,
            purchases.Sum(r => r.Amount),
            purchases.Select(r => r.DocumentId).Distinct().Count(id => Counts(id, OrderType.Purchase)),
            trend,
            Ranked(sales.GroupBy(r => r.ProductId)
                .Select(g => new NamedAmount(g.Key, g.First().Name, g.Sum(r => r.Amount), g.Sum(r => r.Quantity)))),
            Ranked(purchases.GroupBy(r => r.ProductId)
                .Select(g => new NamedAmount(g.Key, g.First().Name, g.Sum(r => r.Amount), g.Sum(r => r.Quantity)))),
            Ranked(sales.GroupBy(r => r.CategoryId)
                .Select(g => new NamedAmount(g.Key, g.First().CategoryName, g.Sum(r => r.Amount), g.Sum(r => r.Quantity)))),
            Ranked(saleAmountByOrder.Keys.GroupBy(id => orders[id].ContactId)
                .Select(g => ByContact(g, id => saleAmountByOrder[id]))),
            Ranked(purchaseAmountByOrder.Keys.GroupBy(id => orders[id].ContactId)
                .Select(g => ByContact(g, id => purchaseAmountByOrder[id]))));
    }

    /// <summary>Largest first; groups that net to zero (e.g. a sale and its cancellation) are dropped.</summary>
    private static List<NamedAmount> Ranked(IEnumerable<NamedAmount> items) =>
        items.Where(x => x.Amount != 0).OrderByDescending(x => x.Amount).ToList();

    // SQLite gives back DateTimeKind.Unspecified; the values are UTC (stamped by AppDbContext).
    private static DateTime Local(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
}

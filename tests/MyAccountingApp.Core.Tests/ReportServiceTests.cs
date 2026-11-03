using MyAccountingApp.Core.Entities;
using MyAccountingApp.Core.Enums;
using MyAccountingApp.Core.Services;

namespace MyAccountingApp.Core.Tests;

public class ReportServiceTests : IDisposable
{
    private readonly TestDb _t = new();

    public void Dispose() => _t.Dispose();

    private async Task<int> ConfirmedAsync(OrderType type, int? contactId, int productId, decimal qty, decimal price)
    {
        var order = await _t.Orders.CreateDraftAsync(type, contactId, [new OrderLineInput(productId, qty, price)]);
        await _t.Orders.ConfirmAsync(order.Id);
        return order.Id;
    }

    [Theory]
    [InlineData(ReportPeriod.Day, 24)]
    [InlineData(ReportPeriod.Week, 7)]
    [InlineData(ReportPeriod.Month, 0)] // days in this month
    [InlineData(ReportPeriod.Year, 12)]
    public async Task Trend_has_one_bucket_per_hour_day_or_month(ReportPeriod period, int buckets)
    {
        if (buckets == 0) buckets = DateTime.DaysInMonth(DateTime.Today.Year, DateTime.Today.Month);
        var report = await _t.Reports.GetDashboardAsync(period);
        Assert.Equal(buckets, report.Trend.Count);
        Assert.Equal(report.FromLocal, report.Trend[0].Start);
        Assert.True(report.ToLocal > DateTime.Now);
    }

    [Fact]
    public async Task Totals_net_out_returns_and_cancellations()
    {
        var (supplier, customer, product) = await _t.SeedAsync();
        await ConfirmedAsync(OrderType.Purchase, supplier.Id, product.Id, 10, 1m);
        await ConfirmedAsync(OrderType.PurchaseReturn, supplier.Id, product.Id, 2, 1m);
        await ConfirmedAsync(OrderType.Sale, customer.Id, product.Id, 4, 3m);
        await ConfirmedAsync(OrderType.Sale, null, product.Id, 1, 3m);
        await ConfirmedAsync(OrderType.SaleReturn, customer.Id, product.Id, 1, 3m);
        var cancelled = await ConfirmedAsync(OrderType.Sale, customer.Id, product.Id, 2, 3m);
        await _t.Orders.CancelAsync(cancelled);

        var report = await _t.Reports.GetDashboardAsync(ReportPeriod.Day);

        Assert.Equal(12m, report.Sales.Revenue);         // (4 + 1 − 1) × 3
        Assert.Equal(4m, report.Sales.Cost);
        Assert.Equal(8m, report.Sales.Profit);
        Assert.Equal(4m, report.Sales.ItemsSold);
        Assert.Equal(2, report.Sales.OrderCount);        // cancelled sale and the return don't count
        Assert.Equal(8m, report.Purchases);              // 10 − 2
        Assert.Equal(1, report.PurchaseCount);

        Assert.Equal(12m, report.Trend.Sum(p => p.Sales));
        Assert.Equal(8m, report.Trend.Sum(p => p.Purchases));
        Assert.Equal(8m, report.Trend.Sum(p => p.Profit));

        var sold = Assert.Single(report.SalesByProduct);
        Assert.Equal((product.Id, "Water", 12m, 4m), (sold.Id!.Value, sold.Name!, sold.Amount, sold.Count));
        Assert.Equal(8m, Assert.Single(report.PurchasesByProduct).Amount);
        Assert.Equal("Drinks", Assert.Single(report.SalesByCategory).Name);
    }

    [Fact]
    public async Task Contacts_are_ranked_with_walk_in_as_null()
    {
        var (supplier, customer, product) = await _t.SeedAsync();
        var general = await _t.Products.SaveAsync(new Product { Name = "Bread", Barcode = "222", Price = 1 }, categoryRequired: false);
        await ConfirmedAsync(OrderType.Purchase, supplier.Id, product.Id, 10, 1m);
        await ConfirmedAsync(OrderType.Purchase, null, general.Id, 10, 1m);
        await ConfirmedAsync(OrderType.Sale, customer.Id, product.Id, 2, 5m);
        await ConfirmedAsync(OrderType.Sale, customer.Id, product.Id, 1, 5m);
        await ConfirmedAsync(OrderType.Sale, null, general.Id, 1, 2m);

        var report = await _t.Reports.GetDashboardAsync(ReportPeriod.Week);

        Assert.Equal(
            [(customer.Id, "Customer A", 15m, 2m), ((int?)null, (string?)null, 2m, 1m)],
            report.SalesByCustomer.Select(c => (c.Id, c.Name, c.Amount, c.Count)));
        Assert.Equal([supplier.Id, null], report.PurchasesBySupplier.Select(c => c.Id));
        Assert.Equal(["Drinks", null], report.SalesByCategory.Select(c => c.Name));
    }

    [Theory]
    [InlineData(ReportPeriod.Day, "2026-11-04", "2026-11-05")]
    [InlineData(ReportPeriod.Week, "2026-10-31", "2026-11-07")]   // Wednesday → from Saturday
    [InlineData(ReportPeriod.Month, "2026-11-01", "2026-12-01")]
    [InlineData(ReportPeriod.Year, "2026-01-01", "2027-01-01")]
    public void Ranges_are_the_current_calendar_period(ReportPeriod period, string from, string to)
    {
        var range = ReportService.RangeOf(period, new DateTime(2026, 11, 4, 15, 0, 0));
        Assert.Equal((DateTime.Parse(from), DateTime.Parse(to)), range);
    }

    [Theory]
    [InlineData("2026-10-31", "2026-10-31")] // Saturday starts its own week
    [InlineData("2026-11-06", "2026-10-31")] // Friday is its last day
    public void Week_starts_on_saturday(string today, string saturday) =>
        Assert.Equal(DateTime.Parse(saturday), ReportService.RangeOf(ReportPeriod.Week, DateTime.Parse(today)).From);

    [Fact]
    public void Year_buckets_are_months() =>
        Assert.Equal(new DateTime(2026, 11, 1), ReportService.BucketOf(ReportPeriod.Year, new DateTime(2026, 11, 30, 23, 0, 0)));
}

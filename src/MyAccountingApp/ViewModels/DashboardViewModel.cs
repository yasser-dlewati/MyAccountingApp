using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAccountingApp.AppServices;
using MyAccountingApp.Controls.Charts;
using MyAccountingApp.Core.Enums;
using MyAccountingApp.Core.Services;
using MyAccountingApp.Helpers;
using MyAccountingApp.Localization;

namespace MyAccountingApp.ViewModels;

public partial class DashboardViewModel(ReportService reports, InventoryService inventory, DialogService dialogs)
    : BaseViewModel(dialogs)
{
    public ObservableCollection<StockLine> LowStock { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDay), nameof(IsWeek), nameof(IsMonth), nameof(IsYear))]
    public partial ReportPeriod Period { get; set; } = ReportPeriod.Day;

    public bool IsDay => Period == ReportPeriod.Day;
    public bool IsWeek => Period == ReportPeriod.Week;
    public bool IsMonth => Period == ReportPeriod.Month;
    public bool IsYear => Period == ReportPeriod.Year;

    [ObservableProperty] public partial string RangeText { get; set; } = "";
    [ObservableProperty] public partial string Revenue { get; set; } = Formats.Money(0);
    [ObservableProperty] public partial string Profit { get; set; } = Formats.Money(0);
    [ObservableProperty] public partial string Purchases { get; set; } = Formats.Money(0);
    [ObservableProperty] public partial string SaleCount { get; set; } = "0";
    [ObservableProperty] public partial string PurchaseCount { get; set; } = "0";
    [ObservableProperty] public partial string ItemsSold { get; set; } = "0";
    [ObservableProperty] public partial string AverageSale { get; set; } = Formats.Money(0);
    [ObservableProperty] public partial string Margin { get; set; } = "0%";

    [ObservableProperty] public partial IReadOnlyList<ChartPoint> SalesTrend { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<ChartPoint> PurchasesTrend { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<ChartPoint> ProfitTrend { get; set; } = [];

    [ObservableProperty] public partial PieData TopSold { get; set; } = PieData.Empty;
    [ObservableProperty] public partial PieData TopPurchased { get; set; } = PieData.Empty;
    [ObservableProperty] public partial PieData SalesByCategory { get; set; } = PieData.Empty;
    [ObservableProperty] public partial PieData SalesByCustomer { get; set; } = PieData.Empty;
    [ObservableProperty] public partial PieData PurchasesBySupplier { get; set; } = PieData.Empty;

    /// <summary>Settings → Categories off: every product is "General", so the category chart is hidden.</summary>
    [ObservableProperty] public partial bool ShowCategories { get; set; }

    partial void OnPeriodChanged(ReportPeriod value) => LoadCommand.Execute(null);

    [RelayCommand]
    private void SelectPeriod(string period) => Period = Enum.Parse<ReportPeriod>(period);

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        var report = await reports.GetDashboardAsync(Period);
        var sales = report.Sales;
        var lastDay = report.ToLocal.AddDays(-1);
        RangeText = Period == ReportPeriod.Day ? Day(report.FromLocal) : $"{Day(report.FromLocal)} – {Day(lastDay)}";

        Revenue = Formats.Money(sales.Revenue);
        Profit = Formats.Money(sales.Profit);
        Purchases = Formats.Money(report.Purchases);
        SaleCount = sales.OrderCount.ToString(CultureInfo.InvariantCulture);
        PurchaseCount = report.PurchaseCount.ToString(CultureInfo.InvariantCulture);
        ItemsSold = Formats.Quantity(sales.ItemsSold);
        AverageSale = Formats.Money(sales.OrderCount == 0 ? 0 : sales.Revenue / sales.OrderCount);
        Margin = Formats.Percent(sales.Revenue == 0 ? 0 : sales.Profit / sales.Revenue);

        SalesTrend = Trend(report.Trend, p => p.Sales);
        PurchasesTrend = Trend(report.Trend, p => p.Purchases);
        ProfitTrend = Trend(report.Trend, p => p.Profit);

        var general = LocalizationService.Get("Category_General");
        TopSold = Pie(report.SalesByProduct, null);
        TopPurchased = Pie(report.PurchasesByProduct, null);
        SalesByCategory = Pie(report.SalesByCategory, general);
        SalesByCustomer = Pie(report.SalesByCustomer, DisplayNames.WalkIn(OrderType.Sale));
        PurchasesBySupplier = Pie(report.PurchasesBySupplier, DisplayNames.WalkIn(OrderType.Purchase));
        ShowCategories = CategorySettings.Enabled;

        LowStock.Clear();
        foreach (var r in await inventory.GetStockReportAsync(StockSettings.LowStockThreshold))
            LowStock.Add(new StockLine(r.ProductName, r.Barcode, Formats.Quantity(r.Quantity),
                Formats.Money(r.LastCost), Formats.Money(r.CostValue), r.Quantity <= 0));
    });

    private static string Day(DateTime local) => local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private List<ChartPoint> Trend(List<TrendPoint> points, Func<TrendPoint, decimal> value) =>
        points.Select((p, i) => new ChartPoint(AxisLabel(p.Start, i, points.Count), (double)value(p),
            $"{BucketName(p.Start)} · {Formats.Money(value(p))}")).ToList();

    /// <summary>A few readable x labels: every 6 hours, every day of a week, weekly back from today, every 2 months.</summary>
    private string AxisLabel(DateTime start, int index, int count)
    {
        var inv = CultureInfo.InvariantCulture;
        return Period switch
        {
            ReportPeriod.Day => index % 6 == 0 ? start.ToString("HH:mm", inv) : "",
            ReportPeriod.Week => start.ToString("MM-dd", inv),
            ReportPeriod.Month => (count - 1 - index) % 7 == 0 ? start.ToString("MM-dd", inv) : "",
            _ => (count - 1 - index) % 2 == 0 ? start.ToString("yy-MM", inv) : "",
        };
    }

    private string BucketName(DateTime start) => Period switch
    {
        ReportPeriod.Day => start.ToString("HH:mm", CultureInfo.InvariantCulture),
        ReportPeriod.Year => start.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        _ => Day(start),
    };

    /// <summary>Top 5 by amount + "Other" (≤ 6 segments); <paramref name="nullName"/> names the null row.</summary>
    private static PieData Pie(List<NamedAmount> items, string? nullName)
    {
        var positive = items.Where(x => x.Amount > 0).ToList();
        var total = positive.Sum(x => x.Amount);
        if (total == 0) return PieData.Empty;

        ChartSlice Slice(string label, decimal amount, Color color) =>
            new(label, (double)amount, Formats.Money(amount), Formats.Percent(amount / total), color);

        var slices = positive.Take(ChartPalette.SlotCount)
            .Select((x, i) => Slice(x.Name ?? nullName ?? "", x.Amount, ChartPalette.Series(i)))
            .ToList();
        var rest = positive.Skip(ChartPalette.SlotCount).Sum(x => x.Amount);
        if (rest > 0)
            slices.Add(Slice(LocalizationService.Get("Dashboard_Other"), rest, ChartPalette.Other));
        return new PieData(slices, Formats.Compact(total));
    }
}

public record StockLine(string Name, string Barcode, string Quantity, string LastCost, string Value, bool IsOutOfStock);

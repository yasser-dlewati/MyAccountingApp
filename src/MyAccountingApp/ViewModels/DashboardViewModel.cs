using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAccountingApp.AppServices;
using MyAccountingApp.Core.Enums;
using MyAccountingApp.Core.Services;
using MyAccountingApp.Helpers;

namespace MyAccountingApp.ViewModels;

public partial class DashboardViewModel(InventoryService inventory, DialogService dialogs) : BaseViewModel(dialogs)
{
    public const decimal LowStockThreshold = 5;

    public ObservableCollection<StockLine> LowStock { get; } = [];

    [ObservableProperty] public partial string TodayRevenue { get; set; } = Formats.Money(0);
    [ObservableProperty] public partial string TodayProfit { get; set; } = Formats.Money(0);
    [ObservableProperty] public partial string TodayOrders { get; set; } = "0";
    [ObservableProperty] public partial string MonthRevenue { get; set; } = Formats.Money(0);
    [ObservableProperty] public partial string MonthProfit { get; set; } = Formats.Money(0);

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);

        var day = await inventory.GetSalesSummaryAsync(today.ToUniversalTime(), today.AddDays(1).ToUniversalTime());
        TodayRevenue = Formats.Money(day.Revenue);
        TodayProfit = Formats.Money(day.Profit);
        TodayOrders = day.OrderCount.ToString();

        var month = await inventory.GetSalesSummaryAsync(monthStart.ToUniversalTime(), monthStart.AddMonths(1).ToUniversalTime());
        MonthRevenue = Formats.Money(month.Revenue);
        MonthProfit = Formats.Money(month.Profit);

        LowStock.Clear();
        foreach (var r in await inventory.GetStockReportAsync(LowStockThreshold))
            LowStock.Add(new StockLine(r.ProductName, r.Barcode, Formats.Quantity(r.Quantity),
                Formats.Money(r.LastCost), Formats.Money(r.CostValue), r.Quantity <= 0 ? Colors.IndianRed : null));
    });

    [RelayCommand]
    private Task NewSaleAsync() => NewOrderAsync(OrderType.Sale);

    [RelayCommand]
    private Task NewPurchaseAsync() => NewOrderAsync(OrderType.Purchase);

    private static Task NewOrderAsync(OrderType type) =>
        GoToAsync(Routes.OrderEdit, new Dictionary<string, object> { ["type"] = type });
}

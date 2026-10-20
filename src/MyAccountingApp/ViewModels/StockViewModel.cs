using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAccountingApp.AppServices;
using MyAccountingApp.Core.Services;
using MyAccountingApp.Helpers;

namespace MyAccountingApp.ViewModels;

public partial class StockViewModel(InventoryService inventory, DialogService dialogs) : BaseViewModel(dialogs)
{
    public ObservableCollection<StockLine> Items { get; } = [];

    [ObservableProperty] public partial string? SearchText { get; set; }
    [ObservableProperty] public partial string TotalValue { get; set; } = Formats.Money(0);

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        var rows = await inventory.GetStockReportAsync();
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var t = SearchText.Trim();
            rows = rows.Where(r => r.ProductName.Contains(t, StringComparison.CurrentCultureIgnoreCase) || r.Barcode.Contains(t)).ToList();
        }

        Items.Clear();
        foreach (var r in rows)
            Items.Add(new StockLine(r.ProductName, r.Barcode, Formats.Quantity(r.Quantity),
                Formats.Money(r.LastCost), Formats.Money(r.CostValue), r.Quantity <= 0 ? Colors.IndianRed : null));
        TotalValue = Formats.Money(rows.Where(r => r.Quantity > 0).Sum(r => r.CostValue));
    });
}

public record StockLine(string Name, string Barcode, string Quantity, string LastCost, string Value, Color? QuantityColor);

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAccountingApp.AppServices;
using MyAccountingApp.Core.Services;
using MyAccountingApp.Helpers;

namespace MyAccountingApp.ViewModels;

public partial class ProductsViewModel(
    ProductService products, InventoryService inventory, QuickScanService quickScan, DialogService dialogs)
    : BaseViewModel(dialogs)
{
    public ObservableCollection<ProductRow> Items { get; } = [];

    [ObservableProperty]
    public partial string? SearchText { get; set; }

    /// <summary>Σ quantity × last cost of the listed products that are in stock.</summary>
    [ObservableProperty]
    public partial string TotalValue { get; set; } = Formats.Money(0);

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        var list = await products.SearchAsync(SearchText, includeInactive: ProductSettings.ShowInactive);
        var stock = (await inventory.GetStockReportAsync()).ToDictionary(r => r.ProductId);
        var showCategory = CategorySettings.Enabled;
        Items.Clear();
        decimal total = 0;
        foreach (var p in list)
        {
            var s = stock.GetValueOrDefault(p.Id); // null for inactive products (not in the report)
            var quantity = s?.Quantity ?? 0;
            if (quantity > 0) total += s!.CostValue;
            Items.Add(new ProductRow(p.Id, p.Name, showCategory ? $"{p.Barcode} · {DisplayNames.CategoryOf(p)}" : p.Barcode,
                Formats.Money(p.Price), Formats.Quantity(quantity), quantity <= 0,
                Formats.Money(s?.LastCost ?? 0), Formats.Money(s?.CostValue ?? 0), p.IsActive ? 1 : 0.45));
        }
        TotalValue = Formats.Money(total);
    });

    [RelayCommand]
    private Task AddAsync() => GoToAsync(Routes.ProductEdit, new Dictionary<string, object> { ["id"] = 0 });

    /// <summary>Scanned barcode → that product's details with an Edit button, or offer to create it.</summary>
    [RelayCommand]
    private Task ScanAsync() => quickScan.CheckProductAsync();

    [RelayCommand]
    private Task OpenAsync(ProductRow row) =>
        GoToAsync(Routes.ProductEdit, new Dictionary<string, object> { ["id"] = row.Id });
}

public record ProductRow(int Id, string Name, string Subtitle, string Price, string Stock, bool IsOutOfStock,
    string LastCost, string Value, double Opacity);

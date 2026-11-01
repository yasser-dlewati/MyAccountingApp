using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAccountingApp.AppServices;
using MyAccountingApp.Core.Services;
using MyAccountingApp.Helpers;
using MyAccountingApp.Localization;

namespace MyAccountingApp.ViewModels;

public partial class ProductsViewModel(
    ProductService products, InventoryService inventory, BarcodeScannerService scanner, DialogService dialogs)
    : BaseViewModel(dialogs)
{
    public ObservableCollection<ProductRow> Items { get; } = [];

    [ObservableProperty]
    public partial string? SearchText { get; set; }

    [ObservableProperty]
    public partial bool ShowInactive { get; set; }

    partial void OnShowInactiveChanged(bool value) => LoadCommand.Execute(null);

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        var list = await products.SearchAsync(SearchText, includeInactive: ShowInactive);
        var stock = (await inventory.GetStockReportAsync()).ToDictionary(r => r.ProductId, r => r.Quantity);
        var showCategory = CategorySettings.Enabled;
        Items.Clear();
        foreach (var p in list)
            Items.Add(new ProductRow(p.Id, p.Name, showCategory ? $"{p.Barcode} · {DisplayNames.CategoryOf(p)}" : p.Barcode,
                Formats.Money(p.Price), Formats.Quantity(stock.GetValueOrDefault(p.Id)), p.IsActive ? 1 : 0.45));
    });

    [RelayCommand]
    private Task AddAsync() => GoToAsync(Routes.ProductEdit, new Dictionary<string, object> { ["id"] = 0 });

    /// <summary>Scanned barcode → open that product (even if inactive), or offer to create it.</summary>
    [RelayCommand]
    private async Task ScanAsync()
    {
        var barcode = await scanner.ScanAsync();
        if (barcode is null) return;

        var product = await products.GetByBarcodeAsync(barcode);
        if (product is not null)
        {
            await GoToAsync(Routes.ProductEdit, new Dictionary<string, object> { ["id"] = product.Id });
            return;
        }
        if (await Dialogs.ConfirmAsync(LocalizationService.Format("Scan_CreateProductPrompt", barcode)))
            await GoToAsync(Routes.ProductEdit, new Dictionary<string, object> { ["id"] = 0, ["barcode"] = barcode });
    }

    [RelayCommand]
    private Task OpenAsync(ProductRow row) =>
        GoToAsync(Routes.ProductEdit, new Dictionary<string, object> { ["id"] = row.Id });
}

public record ProductRow(int Id, string Name, string Subtitle, string Price, string Stock, double Opacity);

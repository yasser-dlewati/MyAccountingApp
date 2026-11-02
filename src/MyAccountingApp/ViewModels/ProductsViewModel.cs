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

    /// <summary>
    /// Scanned barcode → show that product's details (even if inactive) with an Edit button,
    /// or offer to create it.
    /// </summary>
    [RelayCommand]
    private async Task ScanAsync()
    {
        var barcode = await scanner.ScanAsync();
        if (barcode is null) return;

        var found = await products.GetByBarcodeAsync(barcode);
        if (found is null)
        {
            if (await Dialogs.ConfirmAsync(LocalizationService.Format("Scan_CreateProductPrompt", barcode)))
                await GoToAsync(Routes.ProductEdit, new Dictionary<string, object> { ["id"] = 0, ["barcode"] = barcode });
            return;
        }

        var product = await products.GetAsync(found.Id) ?? found;
        var details = LocalizationService.Format("Product_Details",
            product.Barcode,
            Formats.Money(product.Price),
            Formats.Quantity(await inventory.GetStockAsync(product.Id)),
            Formats.Money(await inventory.GetLastPurchaseCostAsync(product.Id)),
            DisplayNames.CategoryOf(product),
            product.Supplier?.Name ?? LocalizationService.Get("Product_NoSupplier"),
            LocalizationService.Get(product.IsActive ? "Product_Active" : "Product_Inactive"));

        if (await Dialogs.DetailsAsync(product.Name, details, LocalizationService.Get("Product_Edit")))
            await GoToAsync(Routes.ProductEdit, new Dictionary<string, object> { ["id"] = product.Id });
    }

    [RelayCommand]
    private Task OpenAsync(ProductRow row) =>
        GoToAsync(Routes.ProductEdit, new Dictionary<string, object> { ["id"] = row.Id });
}

public record ProductRow(int Id, string Name, string Subtitle, string Price, string Stock, bool IsOutOfStock,
    string LastCost, string Value, double Opacity);

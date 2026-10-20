using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAccountingApp.AppServices;
using MyAccountingApp.Core.Services;
using MyAccountingApp.Helpers;

namespace MyAccountingApp.ViewModels;

public partial class ProductsViewModel(ProductService products, InventoryService inventory, DialogService dialogs)
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
        Items.Clear();
        foreach (var p in list)
            Items.Add(new ProductRow(p.Id, p.Name, $"{p.Barcode} · {p.Category?.Name}",
                Formats.Money(p.Price), Formats.Quantity(stock.GetValueOrDefault(p.Id)), p.IsActive ? 1 : 0.45));
    });

    [RelayCommand]
    private Task AddAsync() => GoToAsync(Routes.ProductEdit, new Dictionary<string, object> { ["id"] = 0 });

    [RelayCommand]
    private Task OpenAsync(ProductRow row) =>
        GoToAsync(Routes.ProductEdit, new Dictionary<string, object> { ["id"] = row.Id });
}

public record ProductRow(int Id, string Name, string Subtitle, string Price, string Stock, double Opacity);

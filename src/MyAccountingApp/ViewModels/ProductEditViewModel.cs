using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAccountingApp.AppServices;
using MyAccountingApp.Core.Entities;
using MyAccountingApp.Core.Enums;
using MyAccountingApp.Core.Services;
using MyAccountingApp.Helpers;
using MyAccountingApp.Localization;

namespace MyAccountingApp.ViewModels;

public partial class ProductEditViewModel : BaseViewModel, IQueryAttributable
{
    private readonly ProductService _products;
    private readonly CategoryService _categories;
    private readonly ContactService _contacts;
    private readonly InventoryService _inventory;
    private Product _product = new();
    private readonly Option<int?> _noSupplier = new(LocalizationService.Get("Product_NoSupplier"), null);

    public ProductEditViewModel(ProductService products, CategoryService categories, ContactService contacts,
        InventoryService inventory, DialogService dialogs) : base(dialogs)
    {
        _products = products;
        _categories = categories;
        _contacts = contacts;
        _inventory = inventory;
        Title = LocalizationService.Get("Product_New");
    }

    public ObservableCollection<Category> Categories { get; } = [];
    public ObservableCollection<Option<int?>> Suppliers { get; } = [];
    public ObservableCollection<SupplierCostRow> SupplierCosts { get; } = [];

    [ObservableProperty] public partial string Title { get; set; }
    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string Barcode { get; set; } = "";
    [ObservableProperty] public partial string Price { get; set; } = "";
    [ObservableProperty] public partial Category? Category { get; set; }
    [ObservableProperty] public partial Option<int?>? DefaultSupplier { get; set; }
    [ObservableProperty] public partial bool IsActive { get; set; } = true;
    [ObservableProperty] public partial string? StockInfo { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExisting))]
    public partial int ProductId { get; set; }

    public bool IsExisting => ProductId != 0;

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        await RunAsync(async () =>
        {
            await LoadListsAsync();
            if (query.TryGetValue("id", out var value) && value is int id && id != 0)
                await LoadProductAsync(id);
            else
                DefaultSupplier = _noSupplier;
        });
    }

    private async Task LoadListsAsync()
    {
        Categories.Clear();
        foreach (var c in await _categories.GetAllAsync()) Categories.Add(c);

        Suppliers.Clear();
        Suppliers.Add(_noSupplier);
        foreach (var s in await _contacts.SearchAsync(role: ContactType.Supplier))
            Suppliers.Add(new Option<int?>(s.Name, s.Id));
    }

    private async Task LoadProductAsync(int id)
    {
        _product = await _products.GetAsync(id) ?? throw new BusinessRuleException(ErrorKeys.NotFound);
        ProductId = _product.Id;
        Title = _product.Name;
        Name = _product.Name;
        Barcode = _product.Barcode;
        Price = Formats.Input(_product.Price);
        IsActive = _product.IsActive;

        // An inactive category / supplier still has to show for this product.
        if (_product.Category is not null && Categories.All(c => c.Id != _product.CategoryId))
            Categories.Add(_product.Category);
        Category = Categories.First(c => c.Id == _product.CategoryId);

        if (_product.Supplier is not null && Suppliers.All(s => s.Value != _product.SupplierId))
            Suppliers.Add(new Option<int?>(_product.Supplier.Name, _product.Supplier.Id));
        DefaultSupplier = Suppliers.First(s => s.Value == _product.SupplierId);

        SupplierCosts.Clear();
        foreach (var ps in _product.ProductSuppliers.OrderBy(ps => ps.Contact!.Name))
            SupplierCosts.Add(new SupplierCostRow(ps.ContactId, ps.Contact!.Name, Formats.Money(ps.CostPrice)));

        var stock = await _inventory.GetStockAsync(id);
        var lastCost = await _inventory.GetLastPurchaseCostAsync(id);
        StockInfo = LocalizationService.Format("Product_StockInfo", Formats.Quantity(stock), Formats.Money(lastCost));
    }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        if (!Formats.TryParseDecimal(Price, out var price))
            throw new BusinessRuleException(ErrorKeys.PriceNegative);

        _product.Name = Name;
        _product.Barcode = Barcode;
        _product.Price = price;
        _product.CategoryId = Category?.Id ?? 0;
        _product.SupplierId = DefaultSupplier?.Value;
        _product.IsActive = IsActive;
        await _products.SaveAsync(_product);
        await GoBackAsync();
    });

    [RelayCommand]
    private async Task AddSupplierAsync()
    {
        var candidates = Suppliers.Where(s => s.Value is not null).ToList();
        var name = await Dialogs.ChooseAsync(LocalizationService.Get("Product_AddSupplier"),
            candidates.Select(c => c.Label).ToArray());
        var supplier = candidates.FirstOrDefault(c => c.Label == name);
        if (supplier is null) return;

        var costText = await Dialogs.PromptAsync(LocalizationService.Get("Product_CostPrice"), keyboard: Keyboard.Numeric);
        if (costText is null) return;

        await RunAsync(async () =>
        {
            if (!Formats.TryParseDecimal(costText, out var cost))
                throw new BusinessRuleException(ErrorKeys.PriceNegative);
            await _products.SaveSupplierAsync(_product.Id, supplier.Value!.Value, cost);
            await LoadProductAsync(_product.Id);
        });
    }

    [RelayCommand]
    private async Task RemoveSupplierAsync(SupplierCostRow row)
    {
        if (!await Dialogs.ConfirmAsync(LocalizationService.Format("Product_RemoveSupplierConfirm", row.Name))) return;
        await RunAsync(async () =>
        {
            await _products.RemoveSupplierAsync(_product.Id, row.ContactId);
            await LoadProductAsync(_product.Id);
        });
    }
}

public record SupplierCostRow(int ContactId, string Name, string Cost);

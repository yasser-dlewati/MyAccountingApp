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
    private readonly BarcodeScannerService _scanner;
    private Product _product = new();
    /// <summary>Null supplier = the walk-in (cash) supplier: always listed, even when there are no supplier contacts.</summary>
    private readonly Option<int?> _noSupplier = new(DisplayNames.WalkIn(OrderType.Purchase), null);

    public ProductEditViewModel(ProductService products, CategoryService categories, ContactService contacts,
        InventoryService inventory, BarcodeScannerService scanner, DialogService dialogs) : base(dialogs)
    {
        _products = products;
        _categories = categories;
        _contacts = contacts;
        _inventory = inventory;
        _scanner = scanner;
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
    /// <summary>New products: opening stock quantity and unit cost (blank = 0). Existing products show their stock on hand
    /// and last cost here, read-only (ignored on save).</summary>
    [ObservableProperty] public partial string OpeningQuantity { get; set; } = "";
    [ObservableProperty] public partial string OpeningCost { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExisting), nameof(IsNew), nameof(StockLabel))]
    public partial int ProductId { get; set; }

    public bool IsExisting => ProductId != 0;
    public bool IsNew => !IsExisting;

    public string StockLabel => LocalizationService.Get(IsNew ? "Field_OpeningQuantity" : "Product_InStockNow");

    public bool UseCategories { get; } = CategorySettings.Enabled;

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        await RunAsync(async () =>
        {
            await LoadListsAsync();
            if (query.TryGetValue("id", out var value) && value is int id && id != 0)
                await LoadProductAsync(id);
            else
            {
                DefaultSupplier = _noSupplier;
                // New product from an unknown scanned barcode (Products page).
                if (query.TryGetValue("barcode", out var barcode) && barcode is string b)
                    Barcode = b;
            }
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
        Category = Categories.FirstOrDefault(c => c.Id == _product.CategoryId);

        if (_product.Supplier is not null && Suppliers.All(s => s.Value != _product.SupplierId))
            Suppliers.Add(new Option<int?>(_product.Supplier.Name, _product.Supplier.Id));
        DefaultSupplier = Suppliers.First(s => s.Value == _product.SupplierId);

        SupplierCosts.Clear();
        // Walk-in (cash) supplier first, then by name.
        foreach (var ps in _product.ProductSuppliers.OrderBy(ps => ps.ContactId is not null).ThenBy(ps => ps.Contact?.Name))
            SupplierCosts.Add(new SupplierCostRow(ps.ContactId, ps.Contact?.Name ?? _noSupplier.Label, Formats.Money(ps.CostPrice)));

        var stock = await _inventory.GetStockAsync(id);
        var lastCost = await _inventory.GetLastPurchaseCostAsync(id);
        OpeningQuantity = Formats.Quantity(stock);
        OpeningCost = Formats.Input(lastCost); // plain number: the box shows the currency itself
    }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        if (!Formats.TryParseDecimal(Price, out var price))
            throw new BusinessRuleException(ErrorKeys.PriceNegative);

        _product.Name = Name;
        _product.Barcode = Barcode;
        _product.Price = price;
        // Categories off: keep whatever the product had (new products stay in "General").
        if (UseCategories)
            _product.CategoryId = Category?.Id;
        _product.SupplierId = DefaultSupplier?.Value;
        _product.IsActive = IsActive;

        decimal openingQuantity = 0, openingCost = 0;
        if (IsNew)
        {
            if (!string.IsNullOrWhiteSpace(OpeningQuantity) && !Formats.TryParseDecimal(OpeningQuantity, out openingQuantity))
                throw new BusinessRuleException(ErrorKeys.OpeningQuantityNegative);
            if (!string.IsNullOrWhiteSpace(OpeningCost) && !Formats.TryParseDecimal(OpeningCost, out openingCost))
                throw new BusinessRuleException(ErrorKeys.PriceNegative);
        }
        await _products.SaveAsync(_product, categoryRequired: UseCategories, openingQuantity, openingCost);
        await GoBackAsync();
    });

    [RelayCommand]
    private async Task ScanBarcodeAsync()
    {
        var barcode = await _scanner.ScanAsync();
        if (barcode is not null) Barcode = barcode;
    }

    [RelayCommand]
    private async Task AddSupplierAsync()
    {
        // Walk-in (cash) supplier first, so the list is never empty.
        var candidates = Suppliers.ToList();
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
            await _products.SaveSupplierAsync(_product.Id, supplier.Value, cost);
            await LoadProductAsync(_product.Id);
        });
    }

    [RelayCommand]
    private async Task RemoveSupplierAsync(SupplierCostRow row)
    {
        if (!await Dialogs.ConfirmAsync(LocalizationService.Get("Product_RemoveSupplierTitle"),
                LocalizationService.Format("Product_RemoveSupplierConfirm", row.Name), DialogTone.Danger)) return;
        await RunAsync(async () =>
        {
            await _products.RemoveSupplierAsync(_product.Id, row.ContactId);
            await LoadProductAsync(_product.Id);
        });
    }
}

/// <summary><see cref="ContactId"/> null = the walk-in (cash) supplier.</summary>
public record SupplierCostRow(int? ContactId, string Name, string Cost);

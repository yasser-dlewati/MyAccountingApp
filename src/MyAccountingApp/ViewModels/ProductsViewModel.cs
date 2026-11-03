using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAccountingApp.AppServices;
using MyAccountingApp.Core.Enums;
using MyAccountingApp.Core.Services;
using MyAccountingApp.Helpers;
using MyAccountingApp.Localization;

namespace MyAccountingApp.ViewModels;

/// <summary>Stock filter of the Inventory page: in stock (above the low-stock threshold), low (0 &lt; qty ≤ threshold), out (≤ 0).</summary>
public enum StockLevel { InStock, Low, Out }

public partial class ProductsViewModel : BaseViewModel
{
    private readonly ProductService _products;
    private readonly InventoryService _inventory;
    private readonly QuickScanService _quickScan;

    /// <summary>Supplier filter key of the walk-in (cash) supplier (contact ids start at 1).</summary>
    private const int WalkInSupplier = 0;

    /// <summary>Supplier ids picked in the supplier filter (several allowed; none = every product).</summary>
    private readonly HashSet<int> _selectedSuppliers = [];

    public ProductsViewModel(ProductService products, InventoryService inventory, QuickScanService quickScan, DialogService dialogs)
        : base(dialogs)
    {
        _products = products;
        _inventory = inventory;
        _quickScan = quickScan;
        StockChips =
        [
            new(LocalizationService.Get("Common_All"), null) { IsSelected = true },
            new(LocalizationService.Get("Stock_InStock"), StockLevel.InStock, Colors.SeaGreen),
            new(LocalizationService.Get("Stock_Low"), StockLevel.Low, Color.FromArgb("#F57C00")),
            new(LocalizationService.Get("Stock_Out"), StockLevel.Out, Colors.IndianRed)
        ];
        StatusChips =
        [
            new(LocalizationService.Get("Common_All"), null) { IsSelected = true },
            new(LocalizationService.Get("Product_Active"), true, Colors.SeaGreen),
            new(LocalizationService.Get("Product_Inactive"), false, Colors.Gray)
        ];
    }

    public ObservableCollection<ProductRow> Items { get; } = [];

    /// <summary>All / In stock / Low / Out of stock (single choice), with counts.</summary>
    public List<FilterChip> StockChips { get; }

    /// <summary>All / Active / Inactive (single choice); shown only when Settings → Products lists inactive products.</summary>
    public List<FilterChip> StatusChips { get; }

    /// <summary>One pill per supplier linked to a listed product (default supplier or product_suppliers); multi-select.
    /// A product with no supplier at all, or a cash cost link, counts under the walk-in (cash) supplier pill.</summary>
    public ObservableCollection<FilterChip> SupplierChips { get; } = [];

    [ObservableProperty] public partial string? SearchText { get; set; }

    /// <summary>The filter panel is open; collapsed it shows <see cref="FilterSummary"/>.</summary>
    [ObservableProperty] public partial bool FiltersExpanded { get; set; }

    [ObservableProperty] public partial string FilterSummary { get; set; } = "";
    [ObservableProperty] public partial bool HasActiveFilters { get; set; }
    [ObservableProperty] public partial bool ShowStatusFilter { get; set; }
    [ObservableProperty] public partial bool HasSuppliers { get; set; }

    /// <summary>Σ quantity × last cost of the listed (filtered) products that are in stock.</summary>
    [ObservableProperty]
    public partial string TotalValue { get; set; } = Formats.Money(0);

    private StockLevel? StockFilter => (StockLevel?)StockChips.First(c => c.IsSelected).Value;
    private bool? StatusFilter => (bool?)StatusChips.First(c => c.IsSelected).Value;

    // Clearing the search box (its ✕) doesn't fire SearchCommand.
    partial void OnSearchTextChanged(string? value)
    {
        if (string.IsNullOrEmpty(value)) LoadCommand.Execute(null);
    }

    [RelayCommand]
    private void ToggleFilters() => FiltersExpanded = !FiltersExpanded;

    [RelayCommand]
    private void SelectStock(FilterChip chip)
    {
        if (chip.IsSelected) return;
        FilterChip.Select(StockChips, chip);
        LoadCommand.Execute(null);
    }

    [RelayCommand]
    private void SelectStatus(FilterChip chip)
    {
        if (chip.IsSelected) return;
        FilterChip.Select(StatusChips, chip);
        LoadCommand.Execute(null);
    }

    /// <summary>Supplier pills toggle on / off independently.</summary>
    [RelayCommand]
    private void ToggleSupplier(FilterChip chip)
    {
        var id = (int)chip.Value!;
        if (!_selectedSuppliers.Remove(id)) _selectedSuppliers.Add(id);
        LoadCommand.Execute(null);
    }

    [RelayCommand]
    private void ClearFilters()
    {
        FilterChip.Select(StockChips, StockChips[0]);
        FilterChip.Select(StatusChips, StatusChips[0]);
        _selectedSuppliers.Clear();
        SearchText = null; // reloads when there was a search; otherwise:
        LoadCommand.Execute(null); // ignored while that load runs
    }

    /// <summary>
    /// Loads every product of the search, then filters by stock / status / suppliers client-side. Each bar's counts are
    /// taken over the products the other filters leave (like the Orders page's status counts).
    /// </summary>
    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        ShowStatusFilter = ProductSettings.ShowInactive;
        if (!ShowStatusFilter) FilterChip.Select(StatusChips, StatusChips[0]);

        var list = await _products.SearchAsync(SearchText, includeInactive: ShowStatusFilter);
        var stock = (await _inventory.GetStockReportAsync()).ToDictionary(r => r.ProductId);
        var links = await _products.GetSupplierLinksAsync();
        var suppliersOf = links.ToLookup(l => l.ProductId, l => l.SupplierId ?? WalkInSupplier);
        var walkInName = DisplayNames.WalkIn(OrderType.Purchase);
        var showCategory = CategorySettings.Enabled;
        var lowStock = StockSettings.LowStockThreshold;

        StockLevel LevelOf(decimal quantity) =>
            quantity <= 0 ? StockLevel.Out : quantity <= lowStock ? StockLevel.Low : StockLevel.InStock;

        var rows = list.Select(p =>
        {
            var s = stock.GetValueOrDefault(p.Id); // null for inactive products (not in the report)
            var quantity = s?.Quantity ?? 0;
            var suppliers = suppliersOf[p.Id].ToHashSet();
            if (suppliers.Count == 0) suppliers.Add(WalkInSupplier);
            return (Product: p, Stock: s, Quantity: quantity, Level: LevelOf(quantity), Suppliers: suppliers);
        }).ToList();

        // Suppliers that no listed product has any more drop out of the selection.
        var present = rows.SelectMany(r => r.Suppliers).ToHashSet();
        _selectedSuppliers.IntersectWith(present);

        var stockFilter = StockFilter;
        var statusFilter = StatusFilter;
        bool StockOk(StockLevel level) => stockFilter is null || level == stockFilter;
        bool StatusOk(bool active) => statusFilter is null || active == statusFilter;
        bool SupplierOk(HashSet<int> ids) => _selectedSuppliers.Count == 0 || ids.Overlaps(_selectedSuppliers);

        foreach (var chip in StockChips)
            chip.Count = Formats.Quantity(rows.Count(r => StatusOk(r.Product.IsActive) && SupplierOk(r.Suppliers)
                && (chip.Value is not StockLevel l || r.Level == l)));
        foreach (var chip in StatusChips)
            chip.Count = Formats.Quantity(rows.Count(r => StockOk(r.Level) && SupplierOk(r.Suppliers)
                && (chip.Value is not bool a || r.Product.IsActive == a)));

        SupplierChips.Clear();
        var forSuppliers = rows.Where(r => StockOk(r.Level) && StatusOk(r.Product.IsActive)).ToList();
        var names = links.Where(l => l.SupplierId is not null)
            .DistinctBy(l => l.SupplierId).ToDictionary(l => l.SupplierId!.Value, l => l.SupplierName!);
        // Walk-in (cash) supplier first, then by name.
        foreach (var id in present.OrderBy(id => id != WalkInSupplier).ThenBy(id => names.GetValueOrDefault(id)))
            SupplierChips.Add(new FilterChip(id == WalkInSupplier ? walkInName : names[id], id)
            {
                IsSelected = _selectedSuppliers.Contains(id),
                Count = Formats.Quantity(forSuppliers.Count(r => r.Suppliers.Contains(id)))
            });
        HasSuppliers = SupplierChips.Count > 0;

        Items.Clear();
        decimal total = 0;
        foreach (var r in rows.Where(r => StockOk(r.Level) && StatusOk(r.Product.IsActive) && SupplierOk(r.Suppliers)))
        {
            var p = r.Product;
            if (r.Quantity > 0) total += r.Stock!.CostValue;
            Items.Add(new ProductRow(p.Id, p.Name, showCategory ? $"{p.Barcode} · {DisplayNames.CategoryOf(p)}" : p.Barcode,
                Formats.Money(p.Price), Formats.Quantity(r.Quantity), r.Quantity <= 0, r.Quantity <= lowStock,
                Formats.Money(r.Stock?.LastCost ?? 0), Formats.Money(r.Stock?.CostValue ?? 0), p.IsActive ? 1 : 0.45));
        }
        TotalValue = Formats.Money(total);
        UpdateSummary();
    });

    /// <summary>One line for the collapsed filter header, e.g. "“wat” · Low · 2 suppliers".</summary>
    private void UpdateSummary()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(SearchText)) parts.Add($"“{SearchText.Trim()}”");
        if (StockFilter is not null) parts.Add(StockChips.First(c => c.IsSelected).Label);
        if (StatusFilter is not null) parts.Add(StatusChips.First(c => c.IsSelected).Label);
        if (_selectedSuppliers.Count == 1)
            parts.Add(SupplierChips.First(c => c.IsSelected).Label);
        else if (_selectedSuppliers.Count > 1)
            parts.Add(LocalizationService.Format("Products_SuppliersSelected", Formats.Quantity(_selectedSuppliers.Count)));
        HasActiveFilters = parts.Count > 0;
        FilterSummary = HasActiveFilters ? string.Join(" · ", parts) : LocalizationService.Get("Orders_NoFilters");
    }

    /// <summary>Toolbar barcode button: scanned barcode → that product's details with an Edit button, or offer to create it.</summary>
    [RelayCommand]
    private Task ScanAsync() => _quickScan.CheckProductAsync();

    [RelayCommand]
    private Task OpenAsync(ProductRow row) =>
        GoToAsync(Routes.ProductEdit, new Dictionary<string, object> { ["id"] = row.Id });
}

public record ProductRow(int Id, string Name, string Subtitle, string Price, string Stock, bool IsOutOfStock, bool IsLowStock,
    string LastCost, string Value, double Opacity);

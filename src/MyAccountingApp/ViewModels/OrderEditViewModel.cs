using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAccountingApp.AppServices;
using MyAccountingApp.Core.Entities;
using Contact = MyAccountingApp.Core.Entities.Contact;
using MyAccountingApp.Core.Enums;
using MyAccountingApp.Core.Services;
using MyAccountingApp.Helpers;
using MyAccountingApp.Localization;

namespace MyAccountingApp.ViewModels;

public partial class OrderEditViewModel(
    OrderService orders, ContactService contacts, ProductService products, InventoryService inventory,
    BarcodeScannerService scanner, DialogService dialogs)
    : BaseViewModel(dialogs), IQueryAttributable
{
    private int _orderId;
    // Confirmed order: the quantity per product it already took from stock (given back when it is edited).
    private Dictionary<int, decimal> _postedQuantities = [];

    public ObservableCollection<Contact> Contacts { get; } = [];
    public ObservableCollection<OrderLineViewModel> Lines { get; } = [];
    public ObservableCollection<Product> ProductResults { get; } = [];

    [ObservableProperty] public partial OrderType OrderType { get; set; }
    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string? StatusLabel { get; set; }
    [ObservableProperty] public partial Contact? Contact { get; set; }
    [ObservableProperty] public partial string? ProductSearchText { get; set; }
    [ObservableProperty] public partial string Total { get; set; } = Formats.Money(0);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDraft), nameof(IsConfirmed), nameof(IsEditable), nameof(ShowPostedActions), nameof(CanEdit))]
    public partial OrderStatus Status { get; set; } = OrderStatus.Draft;

    /// <summary>A confirmed order opened for editing (Edit button); saved with <see cref="OrderService.UpdateConfirmedAsync"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable), nameof(ShowPostedActions), nameof(CanEdit))]
    public partial bool IsEditing { get; set; }

    public bool IsDraft => Status == OrderStatus.Draft;
    public bool IsConfirmed => Status == OrderStatus.Confirmed;
    /// <summary>Lines, contact and product search can be changed.</summary>
    public bool IsEditable => IsDraft || IsEditing;
    /// <summary>Confirmed and not being edited: Revoke invoice (+ Edit on sales).</summary>
    public bool ShowPostedActions => IsConfirmed && !IsEditing;
    /// <summary>Confirmed sales (and sale returns) can be edited.</summary>
    public bool CanEdit => ShowPostedActions && OrderService.IsSalesSide(OrderType);

    public string ContactLabel => LocalizationService.Get(OrderService.IsSalesSide(OrderType) ? "Order_Customer" : "Order_Supplier");

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        await RunAsync(async () =>
        {
            if (query.TryGetValue("id", out var id))
            {
                await LoadOrderAsync((int)id);
            }
            else
            {
                OrderType = (OrderType)query["type"];
                Title = LocalizationService.Format("Order_NewOfType", EnumLabels.Of(OrderType));
                StatusLabel = EnumLabels.Of(OrderStatus.Draft);
                await LoadContactsAsync(null);
                // Started from the bottom bar's scan button: the scanned products (a repeated id = quantity + 1).
                if (query.TryGetValue("productIds", out var productIds))
                    foreach (var productId in (List<int>)productIds)
                        if (await products.GetAsync(productId) is { } product)
                            await AddLineAsync(product);
            }
            OnPropertyChanged(nameof(ContactLabel));
        });
    }

    private async Task LoadOrderAsync(int id)
    {
        var order = await orders.GetAsync(id) ?? throw new BusinessRuleException(ErrorKeys.NotFound);
        _orderId = order.Id;
        OrderType = order.OrderType;
        Status = order.Status;
        OnPropertyChanged(nameof(CanEdit));
        Title = $"{EnumLabels.Of(order.OrderType)} {order.OrderNumber}";
        StatusLabel = $"{EnumLabels.Of(order.Status)} · {Formats.Date(order.CreatedAt)}";
        if (order.EditedAt is { } editedAt)
            StatusLabel += " · " + LocalizationService.Format("Order_EditedOn", Formats.Date(editedAt));
        await LoadContactsAsync(order.Contact);

        _postedQuantities = order.Status == OrderStatus.Confirmed
            ? order.Items.GroupBy(i => i.ProductId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity))
            : [];
        Lines.Clear();
        foreach (var item in order.Items)
            Lines.Add(new OrderLineViewModel(item.ProductId, item.Product!.Name, item.Quantity, item.UnitPrice, UpdateTotal));
        UpdateTotal();
    }

    private async Task LoadContactsAsync(Contact? current)
    {
        var role = OrderService.IsSalesSide(OrderType) ? ContactType.Customer : ContactType.Supplier;
        Contacts.Clear();
        // Id 0 = no contact (walk-in), so orders work before any customer / supplier exists.
        // Offered when allowed in settings; always kept on a posted walk-in order so it still shows.
        if (WalkInSettings.IsAllowed(OrderType) || (current is null && !IsDraft))
            Contacts.Add(new Contact { Name = DisplayNames.WalkIn(OrderType) });
        foreach (var c in await contacts.SearchAsync(role: role)) Contacts.Add(c);

        // Keep an inactive contact selectable on an existing order.
        if (current is not null && Contacts.All(c => c.Id != current.Id))
            Contacts.Add(current);
        Contact = Contacts.FirstOrDefault(c => c.Id == (current?.Id ?? 0));
    }

    private void UpdateTotal() =>
        Total = Formats.Money(Lines.Sum(l => l.QuantityValue * Math.Max(l.UnitPriceValue, 0)));

    [RelayCommand]
    private Task SearchProductsAsync() => RunAsync(FindProductAsync);

    /// <summary>Multi-scan: every scanned product is added (again = quantity + 1).</summary>
    [RelayCommand]
    private Task ScanProductAsync() => IsEditable ? scanner.ScanManyAsync(AddScannedAsync) : Task.CompletedTask;

    /// <summary>Adds one scanned barcode's product; returns the message the scanner shows for it.</summary>
    private async Task<ScanOutcome> AddScannedAsync(string barcode)
    {
        try
        {
            var product = await products.GetByBarcodeAsync(barcode);
            if (product is not { IsActive: true })
                return new(false, LocalizationService.Format("Scan_ProductNotFound", barcode), Found: product is not null);
            await AddLineAsync(product);
            var quantity = Lines.Where(l => l.ProductId == product.Id).Sum(l => l.QuantityValue);
            return new(true, LocalizationService.Format("Scan_Added", product.Name, Formats.Quantity(quantity)),
                Found: true, InStock: await CoversAsync(product.Id, quantity));
        }
        catch (BusinessRuleException ex)
        {
            return new(false, LocalizationService.Format(ex.ErrorKey, ex.Args));
        }
    }

    /// <summary>Stock-in orders always; stock-out: stock on hand (+ what this posted order already took) ≥ quantity.</summary>
    private async Task<bool> CoversAsync(int productId, decimal quantity)
    {
        if (OrderService.StockDirection(OrderType) > 0) return true;
        var available = await inventory.GetStockAsync(productId) + _postedQuantities.GetValueOrDefault(productId);
        return available >= quantity;
    }

    private async Task FindProductAsync()
    {
        ProductResults.Clear();
        if (string.IsNullOrWhiteSpace(ProductSearchText)) return;

        var found = await products.SearchAsync(ProductSearchText);
        // A typed barcode that matches exactly is added straight away (again = quantity + 1).
        if (found.Count > 0 && found[0].Barcode == ProductSearchText.Trim())
        {
            await AddLineAsync(found[0]);
            return;
        }
        foreach (var p in found.Take(20)) ProductResults.Add(p);
    }

    [RelayCommand]
    private Task PickProductAsync(Product product) => RunAsync(() => AddLineAsync(product));

    private async Task AddLineAsync(Product product)
    {
        ProductResults.Clear();
        ProductSearchText = null;

        var existing = Lines.FirstOrDefault(l => l.ProductId == product.Id);
        if (existing is not null)
        {
            existing.Quantity = Formats.Input(existing.QuantityValue + 1);
            return;
        }

        // Sales are priced at the selling price, purchases at the last purchase cost.
        var price = OrderService.IsSalesSide(OrderType)
            ? product.Price
            : await inventory.GetLastPurchaseCostAsync(product.Id);
        Lines.Add(new OrderLineViewModel(product.Id, product.Name, 1, price, UpdateTotal));
        UpdateTotal();
    }

    [RelayCommand]
    private void RemoveLine(OrderLineViewModel line)
    {
        Lines.Remove(line);
        UpdateTotal();
    }

    [RelayCommand]
    private Task ConfirmAsync() => RunAsync(async () =>
    {
        await SaveDraftAsync();
        // Drafts may be incomplete; check before asking anything.
        if (Contact is null or { Id: 0 } && !WalkInSettings.IsAllowed(OrderType))
            throw new BusinessRuleException(ErrorKeys.ContactRequired);
        if (Lines.Count == 0)
            throw new BusinessRuleException(ErrorKeys.OrderHasNoItems);
        if (Lines.Any(l => l.QuantityValue <= 0))
            throw new BusinessRuleException(ErrorKeys.QuantityNotPositive);

        var shortages = await orders.GetShortagesAsync(_orderId);
        // Settings → Inventory: selling below zero turned off.
        if (shortages.Count > 0 && !StockSettings.AllowNegative)
            throw new BusinessRuleException(ErrorKeys.InsufficientStock, "\n" + string.Join("\n", shortages.Select(s =>
                $"{s.ProductName}: {Formats.Quantity(s.Available)} / {Formats.Quantity(s.Requested)}")));

        // The only question asked: lines whose cost is above the selling price.
        var belowCost = await orders.GetBelowCostLinesAsync(_orderId);
        if (belowCost.Count > 0)
        {
            var detail = string.Join("\n", belowCost.Select(l =>
                $"{l.ProductName}: {Formats.Money(l.Cost)} / {Formats.Money(l.SellingPrice)}"));
            if (!await Dialogs.ConfirmAsync(LocalizationService.Format("Order_BelowCostConfirm", detail)))
            {
                await LoadOrderAsync(_orderId);
                return;
            }
        }

        await orders.ConfirmAsync(_orderId, allowNegativeStock: true, WalkInSettings.IsAllowed(OrderType));
        await LoadOrderAsync(_orderId);
    });

    /// <summary>
    /// Revoke invoice: a draft is discarded (deleted if it was saved) and the page closes; a confirmed order is
    /// cancelled (its stock movements are reversed).
    /// </summary>
    [RelayCommand]
    private async Task RevokeAsync()
    {
        if (IsDraft)
        {
            if (!await Dialogs.ConfirmAsync(LocalizationService.Get("Order_RevokeDraftPrompt"))) return;
            await RunAsync(async () =>
            {
                if (_orderId != 0)
                    await orders.DeleteDraftAsync(_orderId);
                await GoBackAsync();
            });
            return;
        }

        if (!await Dialogs.ConfirmAsync(LocalizationService.Get("Order_CancelPrompt"))) return;
        await RunAsync(async () =>
        {
            await orders.CancelAsync(_orderId);
            await LoadOrderAsync(_orderId);
        });
    }

    /// <summary>Opens a confirmed order for editing (lines, prices, contact).</summary>
    [RelayCommand]
    private void Edit()
    {
        if (CanEdit) IsEditing = true;
    }

    [RelayCommand]
    private Task SaveEditAsync() => RunAsync(async () => await SaveEditCoreAsync());

    [RelayCommand]
    private Task DiscardEditAsync() => RunAsync(async () =>
    {
        IsEditing = false;
        ProductResults.Clear();
        await LoadOrderAsync(_orderId);
    });

    /// <summary>Same checks as confirming; true when the changes were saved (false = refused or not confirmed).</summary>
    private async Task<bool> SaveEditCoreAsync()
    {
        var input = ValidatedLines();
        if (Contact is null or { Id: 0 } && !WalkInSettings.IsAllowed(OrderType))
            throw new BusinessRuleException(ErrorKeys.ContactRequired);
        if (input.Count == 0)
            throw new BusinessRuleException(ErrorKeys.OrderHasNoItems);
        if (input.Any(l => l.Quantity <= 0))
            throw new BusinessRuleException(ErrorKeys.QuantityNotPositive);

        var belowCost = await orders.GetBelowCostLinesAsync(OrderType, input);
        if (belowCost.Count > 0)
        {
            var detail = string.Join("\n", belowCost.Select(l =>
                $"{l.ProductName}: {Formats.Money(l.Cost)} / {Formats.Money(l.SellingPrice)}"));
            if (!await Dialogs.ConfirmAsync(LocalizationService.Format("Order_BelowCostConfirm", detail)))
                return false;
        }

        int? contactId = Contact is { Id: not 0 } c ? c.Id : null;
        await orders.UpdateConfirmedAsync(_orderId, contactId, input,
            allowNegativeStock: StockSettings.AllowNegative, allowWalkIn: WalkInSettings.IsAllowed(OrderType));
        IsEditing = false;
        await LoadOrderAsync(_orderId);
        return true;
    }

    /// <summary>
    /// Back (toolbar arrow or system back): a draft is kept as it is when it has something worth keeping
    /// (an item or a real, non walk-in contact); otherwise it is dropped (deleted if it was already saved).
    /// </summary>
    [RelayCommand]
    private Task BackAsync() => RunAsync(async () =>
    {
        // Editing a confirmed order: ask whether to keep the changes (No = discard them).
        if (IsEditing && await Dialogs.ConfirmAsync(LocalizationService.Get("Order_SaveEditPrompt"))
                      && !await SaveEditCoreAsync())
            return;

        if (IsDraft)
        {
            if (Lines.Count > 0 || Contact is { Id: not 0 })
                await SaveDraftAsync();
            else if (_orderId != 0)
                await orders.DeleteDraftAsync(_orderId);
        }
        await GoBackAsync();
    });

    private List<OrderLineInput> ValidatedLines()
    {
        if (Lines.Any(l => l.QuantityValue < 0))
            throw new BusinessRuleException(ErrorKeys.QuantityNotPositive);
        if (Lines.Any(l => l.UnitPriceValue < 0))
            throw new BusinessRuleException(ErrorKeys.PriceNegative);
        return Lines.Select(l => new OrderLineInput(l.ProductId, l.QuantityValue, l.UnitPriceValue)).ToList();
    }

    private async Task SaveDraftAsync()
    {
        var input = ValidatedLines();
        int? contactId = Contact is { Id: not 0 } c ? c.Id : null;
        if (_orderId == 0)
            _orderId = (await orders.CreateDraftAsync(OrderType, contactId, input)).Id;
        else
            await orders.UpdateDraftAsync(_orderId, contactId, input);
    }
}

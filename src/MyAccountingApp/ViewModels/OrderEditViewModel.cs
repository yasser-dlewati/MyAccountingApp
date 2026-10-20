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
    OrderService orders, ContactService contacts, ProductService products, InventoryService inventory, DialogService dialogs)
    : BaseViewModel(dialogs), IQueryAttributable
{
    private int _orderId;

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
    [NotifyPropertyChangedFor(nameof(IsDraft), nameof(IsConfirmed), nameof(CanDelete))]
    public partial OrderStatus Status { get; set; } = OrderStatus.Draft;

    public bool IsDraft => Status == OrderStatus.Draft;
    public bool IsConfirmed => Status == OrderStatus.Confirmed;
    public bool CanDelete => IsDraft && _orderId != 0;

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
        Title = $"{EnumLabels.Of(order.OrderType)} {order.OrderNumber}";
        StatusLabel = $"{EnumLabels.Of(order.Status)} · {Formats.Date(order.CreatedAt)}";
        OnPropertyChanged(nameof(CanDelete));
        await LoadContactsAsync(order.Contact);

        Lines.Clear();
        foreach (var item in order.Items)
            Lines.Add(new OrderLineViewModel(item.ProductId, item.Product!.Name, item.Quantity, item.UnitPrice, UpdateTotal));
        UpdateTotal();
    }

    private async Task LoadContactsAsync(Contact? current)
    {
        var role = OrderService.IsSalesSide(OrderType) ? ContactType.Customer : ContactType.Supplier;
        Contacts.Clear();
        foreach (var c in await contacts.SearchAsync(role: role)) Contacts.Add(c);

        // Keep an inactive contact selectable on an existing order.
        if (current is not null && Contacts.All(c => c.Id != current.Id))
            Contacts.Add(current);
        Contact = current is null ? null : Contacts.First(c => c.Id == current.Id);
    }

    private void UpdateTotal() =>
        Total = Formats.Money(Lines.Sum(l => l.QuantityValue * Math.Max(l.UnitPriceValue, 0)));

    [RelayCommand]
    private Task SearchProductsAsync() => RunAsync(async () =>
    {
        ProductResults.Clear();
        if (string.IsNullOrWhiteSpace(ProductSearchText)) return;

        var found = await products.SearchAsync(ProductSearchText);
        // A scanned / typed barcode that matches exactly is added straight away.
        if (found.Count > 0 && found[0].Barcode == ProductSearchText.Trim())
        {
            await AddLineAsync(found[0]);
            return;
        }
        foreach (var p in found.Take(20)) ProductResults.Add(p);
    });

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
    private Task SaveAsync() => RunAsync(async () =>
    {
        await SaveDraftAsync();
        await LoadOrderAsync(_orderId);
    });

    [RelayCommand]
    private Task ConfirmAsync() => RunAsync(async () =>
    {
        await SaveDraftAsync();

        var allowNegative = false;
        var shortages = await orders.GetShortagesAsync(_orderId);
        if (shortages.Count > 0)
        {
            var detail = string.Join("\n", shortages.Select(s =>
                $"{s.ProductName}: {Formats.Quantity(s.Available)} / {Formats.Quantity(s.Requested)}"));
            if (!await Dialogs.ConfirmAsync(LocalizationService.Format("Order_ShortageConfirm", detail)))
            {
                await LoadOrderAsync(_orderId);
                return;
            }
            allowNegative = true;
        }
        else if (!await Dialogs.ConfirmAsync(LocalizationService.Get("Order_ConfirmPrompt")))
        {
            await LoadOrderAsync(_orderId);
            return;
        }

        await orders.ConfirmAsync(_orderId, allowNegative);
        await LoadOrderAsync(_orderId);
    });

    [RelayCommand]
    private async Task CancelOrderAsync()
    {
        if (!await Dialogs.ConfirmAsync(LocalizationService.Get("Order_CancelPrompt"))) return;
        await RunAsync(async () =>
        {
            await orders.CancelAsync(_orderId);
            await LoadOrderAsync(_orderId);
        });
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!await Dialogs.ConfirmAsync(LocalizationService.Get("Order_DeletePrompt"))) return;
        await RunAsync(async () =>
        {
            await orders.DeleteDraftAsync(_orderId);
            await GoBackAsync();
        });
    }

    private async Task SaveDraftAsync()
    {
        if (Contact is null)
            throw new BusinessRuleException(ErrorKeys.ContactRequired);
        if (Lines.Any(l => l.QuantityValue <= 0))
            throw new BusinessRuleException(ErrorKeys.QuantityNotPositive);
        if (Lines.Any(l => l.UnitPriceValue < 0))
            throw new BusinessRuleException(ErrorKeys.PriceNegative);

        var input = Lines.Select(l => new OrderLineInput(l.ProductId, l.QuantityValue, l.UnitPriceValue)).ToList();
        if (_orderId == 0)
            _orderId = (await orders.CreateDraftAsync(OrderType, Contact.Id, input)).Id;
        else
            await orders.UpdateDraftAsync(_orderId, Contact.Id, input);
    }
}

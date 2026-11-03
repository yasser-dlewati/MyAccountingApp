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

public partial class OrdersViewModel : BaseViewModel
{
    private readonly OrderService _orders;

    public OrdersViewModel(OrderService orders, DialogService dialogs) : base(dialogs)
    {
        _orders = orders;
        StatusChips =
        [
            new(LocalizationService.Get("Common_All"), null) { IsSelected = true },
            .. Enum.GetValues<OrderStatus>().Select(s => new FilterChip(EnumLabels.Of(s), s, OrderRow.ColorOf(s)))
        ];
    }

    public ObservableCollection<OrderRow> Items { get; } = [];

    /// <summary>Status filter pills (All / Draft / Confirmed / Cancelled) with the count of each in the current tab + search.</summary>
    public List<FilterChip> StatusChips { get; }

    private OrderStatus? StatusFilter => (OrderStatus?)StatusChips.First(c => c.IsSelected).Value;

    /// <summary>Selected tab: true = sales (+ sale returns), false = purchases (+ purchase returns).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPurchasesTab))]
    public partial bool IsSalesTab { get; set; } = true;

    public bool IsPurchasesTab => !IsSalesTab;

    [ObservableProperty] public partial string? SearchText { get; set; }

    partial void OnIsSalesTabChanged(bool value) => LoadCommand.Execute(null);

    [RelayCommand]
    private void ShowSales() => IsSalesTab = true;

    [RelayCommand]
    private void ShowPurchases() => IsSalesTab = false;

    [RelayCommand]
    private void SelectStatus(FilterChip chip)
    {
        if (chip.IsSelected) return;
        FilterChip.Select(StatusChips, chip);
        LoadCommand.Execute(null);
    }

    /// <summary>Loads every status of the tab + search (to count them) and lists the selected one.</summary>
    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        var list = await _orders.GetOrdersAsync(search: SearchText, salesSide: IsSalesTab);
        foreach (var chip in StatusChips)
            chip.Count = Formats.Quantity(chip.Value is OrderStatus s ? list.Count(o => o.Status == s) : list.Count);

        var status = StatusFilter;
        Items.Clear();
        foreach (var o in list.Where(o => status is null || o.Status == status)) Items.Add(new OrderRow(o));
    });

    /// <summary>New order of the selected tab's side (the order itself or a return).</summary>
    [RelayCommand]
    private async Task AddAsync()
    {
        var types = Enum.GetValues<OrderType>().Where(t => t != OrderType.OpeningStock && OrderService.IsSalesSide(t) == IsSalesTab).ToArray();
        var choice = await Dialogs.ChooseAsync(LocalizationService.Get("Order_New"), types.Select(EnumLabels.Of).ToArray());
        if (choice is null) return;
        var type = types.First(t => EnumLabels.Of(t) == choice);
        await GoToAsync(Routes.OrderEdit, new Dictionary<string, object> { ["type"] = type });
    }

    [RelayCommand]
    private Task OpenAsync(OrderRow row) =>
        GoToAsync(Routes.OrderEdit, new Dictionary<string, object> { ["id"] = row.Order.Id });
}

public record OrderRow(Order Order)
{
    public string Title => $"{Order.OrderNumber} · {DisplayNames.ContactOf(Order)}";
    public string Subtitle => $"{EnumLabels.Of(Order.OrderType)} · {EnumLabels.Of(Order.Status)} · {Formats.Date(Order.CreatedAt)}"
        + (Order.EditedAt is null ? "" : $" · {LocalizationService.Get("Order_Edited")}");
    public string Total => Formats.Money(Order.TotalAmount);
    public Color StatusColor => ColorOf(Order.Status);

    public static Color ColorOf(OrderStatus status) => status switch
    {
        OrderStatus.Confirmed => Colors.SeaGreen,
        OrderStatus.Cancelled => Colors.IndianRed,
        _ => Colors.Gray
    };
}

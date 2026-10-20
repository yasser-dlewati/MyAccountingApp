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
        TypeOptions = [new(LocalizationService.Get("Common_All"), null), .. Enum.GetValues<OrderType>().Select(t => new Option<OrderType?>(EnumLabels.Of(t), t))];
        StatusOptions = [new(LocalizationService.Get("Common_All"), null), .. Enum.GetValues<OrderStatus>().Select(s => new Option<OrderStatus?>(EnumLabels.Of(s), s))];
        TypeFilter = TypeOptions[0];
        StatusFilter = StatusOptions[0];
    }

    public ObservableCollection<OrderRow> Items { get; } = [];
    public List<Option<OrderType?>> TypeOptions { get; }
    public List<Option<OrderStatus?>> StatusOptions { get; }

    [ObservableProperty] public partial Option<OrderType?> TypeFilter { get; set; }
    [ObservableProperty] public partial Option<OrderStatus?> StatusFilter { get; set; }
    [ObservableProperty] public partial string? SearchText { get; set; }

    partial void OnTypeFilterChanged(Option<OrderType?> value) => LoadCommand.Execute(null);
    partial void OnStatusFilterChanged(Option<OrderStatus?> value) => LoadCommand.Execute(null);

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        var list = await _orders.GetOrdersAsync(TypeFilter?.Value, StatusFilter?.Value, search: SearchText);
        Items.Clear();
        foreach (var o in list) Items.Add(new OrderRow(o));
    });

    [RelayCommand]
    private async Task AddAsync()
    {
        var types = Enum.GetValues<OrderType>();
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
    public string Title => $"{Order.OrderNumber} · {Order.Contact?.Name}";
    public string Subtitle => $"{EnumLabels.Of(Order.OrderType)} · {EnumLabels.Of(Order.Status)} · {Formats.Date(Order.CreatedAt)}";
    public string Total => Formats.Money(Order.TotalAmount);
    public Color StatusColor => Order.Status switch
    {
        OrderStatus.Confirmed => Colors.SeaGreen,
        OrderStatus.Cancelled => Colors.IndianRed,
        _ => Colors.Gray
    };
}

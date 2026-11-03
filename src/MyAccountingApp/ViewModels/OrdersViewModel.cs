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

public partial class OrdersViewModel : BaseViewModel, IQueryAttributable
{
    private readonly OrderService _orders;
    private bool _reloadPending;
    private bool _applyingQuery;
    private bool _openedWithQuery;

    /// <summary>The date range is untouched: no date bound; From shows the oldest order's date, To today.</summary>
    private bool _allDates = true;

    public OrdersViewModel(OrderService orders, DialogService dialogs) : base(dialogs)
    {
        _orders = orders;
        StatusChips =
        [
            new(LocalizationService.Get("Common_All"), null) { IsSelected = true },
            .. Enum.GetValues<OrderStatus>().Select(s => new FilterChip(EnumLabels.Of(s), s, OrderRow.ColorOf(s)))
        ];
        ResetDateRange();
    }

    public ObservableCollection<OrderRow> Items { get; } = [];

    /// <summary>Status filter pills (All / Draft / Confirmed / Cancelled) with the count of each in the current tab + search + dates.</summary>
    public List<FilterChip> StatusChips { get; }

    private OrderStatus? StatusFilter => (OrderStatus?)StatusChips.First(c => c.IsSelected).Value;

    /// <summary>Selected tab: true = sales (+ sale returns), false = purchases (+ purchase returns).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPurchasesTab))]
    public partial bool IsSalesTab { get; set; } = true;

    public bool IsPurchasesTab => !IsSalesTab;

    [ObservableProperty] public partial string? SearchText { get; set; }

    /// <summary>The filter panel (search, status, date range) is open; collapsed it shows <see cref="FilterSummary"/>.</summary>
    [ObservableProperty] public partial bool FiltersExpanded { get; set; }

    /// <summary>Only orders created from the start of From to the end of To (local dates, both inclusive).
    /// Until the user picks a date they span every order (<see cref="_allDates"/>).</summary>
    [ObservableProperty] public partial DateTime? FromDate { get; set; }
    [ObservableProperty] public partial DateTime? ToDate { get; set; }

    [ObservableProperty] public partial string FilterSummary { get; set; } = "";
    [ObservableProperty] public partial bool HasActiveFilters { get; set; }
    [ObservableProperty] public partial bool IsDateRangeInvalid { get; set; }

    partial void OnIsSalesTabChanged(bool value) => Reload();
    partial void OnFromDateChanged(DateTime? value) => OnDatePicked();
    partial void OnToDateChanged(DateTime? value) => OnDatePicked();

    // Clearing the search box (its ✕) doesn't fire SearchCommand.
    partial void OnSearchTextChanged(string? value)
    {
        if (string.IsNullOrEmpty(value)) Reload();
    }

    private void OnDatePicked()
    {
        if (_applyingQuery) return;
        _allDates = false;
        Reload();
    }

    private void Reload()
    {
        if (!_applyingQuery) LoadCommand.Execute(null);
    }

    /// <summary>
    /// Opened from the dashboard's invoice counts: <c>salesSide</c> (bool), optional <c>status</c> (<see cref="OrderStatus"/>)
    /// and <c>fromLocal</c> / <c>toLocal</c> (local times, <c>toLocal</c> exclusive). Other filters are reset.
    /// </summary>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("salesSide", out var side) || side is not bool salesSide) return;
        _openedWithQuery = true;
        _applyingQuery = true;
        try
        {
            IsSalesTab = salesSide;
            SearchText = null;
            var status = query.TryGetValue("status", out var s) ? s as OrderStatus? : null;
            FilterChip.Select(StatusChips, StatusChips.First(c => Equals(c.Value, status)));
            if (query.TryGetValue("fromLocal", out var f) && f is DateTime from
                && query.TryGetValue("toLocal", out var t) && t is DateTime to)
            {
                (FromDate, ToDate) = (from.Date, to.AddTicks(-1).Date); // To is inclusive
                _allDates = false;
            }
            else
            {
                ResetDateRange();
            }
        }
        finally
        {
            _applyingQuery = false;
        }
        LoadCommand.Execute(null);
    }

    /// <summary>
    /// Called by the page on every arrival. Back from an opened order (<paramref name="returning"/>) keeps the filters;
    /// the dashboard's invoice cards set them (<see cref="ApplyQueryAttributes"/>, which Shell runs first); any other
    /// arrival (Invoices tab) resets the date range to every order.
    /// </summary>
    public void OnNavigatedTo(bool returning)
    {
        if (returning) return;
        if (_openedWithQuery)
        {
            _openedWithQuery = false;
            return;
        }
        if (_allDates) return;
        _applyingQuery = true;
        try
        {
            ResetDateRange();
        }
        finally
        {
            _applyingQuery = false;
        }
        LoadCommand.Execute(null);
    }

    [RelayCommand]
    private void ShowSales() => IsSalesTab = true;

    [RelayCommand]
    private void ShowPurchases() => IsSalesTab = false;

    [RelayCommand]
    private void ToggleFilters() => FiltersExpanded = !FiltersExpanded;

    [RelayCommand]
    private void SelectStatus(FilterChip chip)
    {
        if (chip.IsSelected) return;
        FilterChip.Select(StatusChips, chip);
        LoadCommand.Execute(null);
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _applyingQuery = true;
        try
        {
            SearchText = null;
            FilterChip.Select(StatusChips, StatusChips[0]);
            ResetDateRange();
        }
        finally
        {
            _applyingQuery = false;
        }
        LoadCommand.Execute(null);
    }

    /// <summary>
    /// Loads every status of the tab + search + dates (to count them) and lists the selected one.
    /// A load requested while one runs (e.g. filters changed meanwhile) runs again right after it.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            _reloadPending = true;
            return;
        }
        do
        {
            _reloadPending = false;
            await RunAsync(LoadOnceAsync);
        } while (_reloadPending);
    }

    private async Task LoadOnceAsync()
    {
        UpdateSummary();
        var (from, to) = DateRange();
        IsDateRangeInvalid = from is not null && to is not null && from >= to;
        var showAll = _allDates;

        var list = IsDateRangeInvalid
            ? []
            : await _orders.GetOrdersAsync(search: SearchText, salesSide: IsSalesTab,
                fromUtc: from?.ToUniversalTime(), toUtc: to?.ToUniversalTime());
        if (showAll && _allDates) ShowAllDates(list);
        foreach (var chip in StatusChips)
            chip.Count = Formats.Quantity(chip.Value is OrderStatus s ? list.Count(o => o.Status == s) : list.Count);

        var status = StatusFilter;
        Items.Clear();
        foreach (var o in list.Where(o => status is null || o.Status == status)) Items.Add(new OrderRow(o));
    }

    /// <summary>Local [from, to) of the date filter (whole days), nulls while it spans every order.</summary>
    private (DateTime? From, DateTime? To) DateRange()
    {
        if (_allDates) return (null, null);
        var from = (FromDate ?? DateTime.Today).Date;
        var to = (ToDate ?? DateTime.Today).Date.AddDays(1);
        return (DateTime.SpecifyKind(from, DateTimeKind.Local), DateTime.SpecifyKind(to, DateTimeKind.Local));
    }

    /// <summary>One line for the collapsed filter header, e.g. "“ali” · Confirmed · 2026-11-01 – 2026-11-03".</summary>
    private void UpdateSummary()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(SearchText)) parts.Add($"“{SearchText.Trim()}”");
        if (StatusFilter is OrderStatus status) parts.Add(EnumLabels.Of(status));
        var (from, to) = DateRange();
        if (from is DateTime f && to is DateTime t)
            parts.Add(f == t.AddDays(-1) ? Formats.LocalDate(f) : $"{Formats.LocalDate(f)} – {Formats.LocalDate(t.AddDays(-1))}");
        HasActiveFilters = parts.Count > 0;
        FilterSummary = HasActiveFilters ? string.Join(" · ", parts) : LocalizationService.Get("Orders_NoFilters");
    }

    /// <summary>Back to every order: From / To show today until the load fills in the oldest order's date.</summary>
    private void ResetDateRange()
    {
        _allDates = true;
        (FromDate, ToDate) = (DateTime.Today, DateTime.Today);
    }

    /// <summary>Unfiltered range: From = the oldest listed order's day, To = today (or that day, if later).</summary>
    private void ShowAllDates(List<Order> list)
    {
        var first = list.Count == 0 ? DateTime.Today : list.Min(o => o.CreatedAt).ToLocalTime().Date;
        var last = list.Count == 0 ? DateTime.Today : list.Max(o => o.CreatedAt).ToLocalTime().Date;
        _applyingQuery = true;
        try
        {
            (FromDate, ToDate) = (first < DateTime.Today ? first : DateTime.Today, last > DateTime.Today ? last : DateTime.Today);
        }
        finally
        {
            _applyingQuery = false;
        }
    }

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

    /// <summary>
    /// Swipe action, same prompts as the order page's Revoke invoice: a draft is deleted, a confirmed order is cancelled
    /// (its stock movements reversed). The list (and the chip counts) reload afterwards.
    /// </summary>
    [RelayCommand]
    private async Task SwipeActionAsync(OrderRow row)
    {
        if (!row.CanSwipe) return;
        var draft = row.IsDraft;
        if (!await Dialogs.ConfirmAsync(LocalizationService.Get(draft ? "Order_RevokeDraftPrompt" : "Order_CancelPrompt")))
            return;
        await RunAsync(async () =>
        {
            if (draft) await _orders.DeleteDraftAsync(row.Order.Id);
            else await _orders.CancelAsync(row.Order.Id);
        });
        await LoadAsync();
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

    /// <summary>Swipe left: Delete a draft, Revoke (cancel) a confirmed order; cancelled orders have no action.</summary>
    public bool CanSwipe => Order.Status is OrderStatus.Draft or OrderStatus.Confirmed;
    public bool IsDraft => Order.Status == OrderStatus.Draft;
    public bool IsConfirmed => Order.Status == OrderStatus.Confirmed;
    public string SwipeLabel => LocalizationService.Get(IsDraft ? "Common_Delete" : "Order_Revoke");

    public static Color ColorOf(OrderStatus status) => status switch
    {
        OrderStatus.Confirmed => Colors.SeaGreen,
        OrderStatus.Cancelled => Colors.IndianRed,
        _ => Colors.Gray
    };
}

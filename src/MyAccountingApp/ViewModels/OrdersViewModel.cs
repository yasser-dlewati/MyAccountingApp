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

    /// <summary>Only orders created between From and To (local date + time, both inclusive to the minute).</summary>
    [ObservableProperty] public partial bool UseDateRange { get; set; }
    [ObservableProperty] public partial DateTime? FromDate { get; set; }
    [ObservableProperty] public partial TimeSpan? FromTime { get; set; }
    [ObservableProperty] public partial DateTime? ToDate { get; set; }
    [ObservableProperty] public partial TimeSpan? ToTime { get; set; }

    [ObservableProperty] public partial string FilterSummary { get; set; } = "";
    [ObservableProperty] public partial bool HasActiveFilters { get; set; }
    [ObservableProperty] public partial bool IsDateRangeInvalid { get; set; }

    partial void OnIsSalesTabChanged(bool value) => Reload();
    partial void OnUseDateRangeChanged(bool value) => Reload();
    partial void OnFromDateChanged(DateTime? value) => ReloadIfDated();
    partial void OnFromTimeChanged(TimeSpan? value) => ReloadIfDated();
    partial void OnToDateChanged(DateTime? value) => ReloadIfDated();
    partial void OnToTimeChanged(TimeSpan? value) => ReloadIfDated();

    // Clearing the search box (its ✕) doesn't fire SearchCommand.
    partial void OnSearchTextChanged(string? value)
    {
        if (string.IsNullOrEmpty(value)) Reload();
    }

    private void ReloadIfDated()
    {
        if (UseDateRange) Reload();
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
                var last = to.AddMinutes(-1); // the To fields are inclusive
                (FromDate, FromTime, ToDate, ToTime) = (from.Date, from.TimeOfDay, last.Date, TimeOfMinute(last));
                UseDateRange = true;
            }
            else
            {
                UseDateRange = false;
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
    /// arrival (Invoices tab) turns the date filter off.
    /// </summary>
    public void OnNavigatedTo(bool returning)
    {
        if (returning) return;
        if (_openedWithQuery)
        {
            _openedWithQuery = false;
            return;
        }
        if (!UseDateRange) return;
        _applyingQuery = true;
        try
        {
            UseDateRange = false;
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
            UseDateRange = false;
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

        var list = IsDateRangeInvalid
            ? []
            : await _orders.GetOrdersAsync(search: SearchText, salesSide: IsSalesTab,
                fromUtc: from?.ToUniversalTime(), toUtc: to?.ToUniversalTime());
        foreach (var chip in StatusChips)
            chip.Count = Formats.Quantity(chip.Value is OrderStatus s ? list.Count(o => o.Status == s) : list.Count);

        var status = StatusFilter;
        Items.Clear();
        foreach (var o in list.Where(o => status is null || o.Status == status)) Items.Add(new OrderRow(o));
    }

    /// <summary>Local [from, to) of the date filter (To is inclusive to the minute in the UI), nulls when it is off.</summary>
    private (DateTime? From, DateTime? To) DateRange()
    {
        if (!UseDateRange) return (null, null);
        var from = (FromDate ?? DateTime.Today).Date + (FromTime ?? TimeSpan.Zero);
        var to = (ToDate ?? DateTime.Today).Date + (ToTime ?? new TimeSpan(23, 59, 0));
        return (DateTime.SpecifyKind(from, DateTimeKind.Local), DateTime.SpecifyKind(to.AddMinutes(1), DateTimeKind.Local));
    }

    /// <summary>One line for the collapsed filter header, e.g. "“ali” · Confirmed · 2026-11-01 00:00 – 2026-11-03 23:59".</summary>
    private void UpdateSummary()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(SearchText)) parts.Add($"“{SearchText.Trim()}”");
        if (StatusFilter is OrderStatus status) parts.Add(EnumLabels.Of(status));
        var (from, to) = DateRange();
        if (from is DateTime f && to is DateTime t)
            parts.Add($"{Formats.LocalDateTime(f)} – {Formats.LocalDateTime(t.AddMinutes(-1))}");
        HasActiveFilters = parts.Count > 0;
        FilterSummary = HasActiveFilters ? string.Join(" · ", parts) : LocalizationService.Get("Orders_NoFilters");
    }

    /// <summary>Date range defaults: today 00:00 – 23:59.</summary>
    private void ResetDateRange()
    {
        (FromDate, FromTime) = (DateTime.Today, TimeSpan.Zero);
        (ToDate, ToTime) = (DateTime.Today, new TimeSpan(23, 59, 0));
    }

    private static TimeSpan TimeOfMinute(DateTime t) => new(t.Hour, t.Minute, 0);

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

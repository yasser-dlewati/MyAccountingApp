using CommunityToolkit.Mvvm.ComponentModel;
using MyAccountingApp.Helpers;

namespace MyAccountingApp.ViewModels;

public partial class OrderLineViewModel(int productId, string productName, decimal quantity, decimal unitPrice, Action changed)
    : ObservableObject
{
    public int ProductId { get; } = productId;
    public string ProductName { get; } = productName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineTotal))]
    public partial string Quantity { get; set; } = Formats.Input(quantity);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineTotal))]
    public partial string UnitPrice { get; set; } = Formats.Input(unitPrice);

    public decimal QuantityValue => Formats.TryParseDecimal(Quantity, out var q) ? q : 0;
    // Blank = 0 (a draft may be saved unfinished); other unparseable text = -1 (rejected as negative).
    public decimal UnitPriceValue => string.IsNullOrWhiteSpace(UnitPrice) ? 0
        : Formats.TryParseDecimal(UnitPrice, out var p) ? p : -1;

    public string LineTotal => Formats.Money(QuantityValue * Math.Max(UnitPriceValue, 0));

    partial void OnQuantityChanged(string value) => changed();
    partial void OnUnitPriceChanged(string value) => changed();
}

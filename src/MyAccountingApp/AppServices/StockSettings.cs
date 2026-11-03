namespace MyAccountingApp.AppServices;

/// <summary>
/// Settings → Inventory, saved in Preferences. Allow negative stock on: a sale / purchase return may take more than the
/// stock on hand. Off: confirming such an order is refused. Low stock: a product at or below this quantity is "low"
/// (dashboard list, highlighted on the Inventory page).
/// </summary>
public static class StockSettings
{
    private const string AllowNegativeKey = "allow_negative_stock";
    private const string LowStockKey = "low_stock_threshold";
    public const decimal DefaultLowStockThreshold = 5;

    public static bool AllowNegative
    {
        get => Preferences.Default.Get(AllowNegativeKey, true);
        set => Preferences.Default.Set(AllowNegativeKey, value);
    }

    public static decimal LowStockThreshold
    {
        get => decimal.TryParse(Preferences.Default.Get(LowStockKey, ""), System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : DefaultLowStockThreshold;
        set => Preferences.Default.Set(LowStockKey, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}

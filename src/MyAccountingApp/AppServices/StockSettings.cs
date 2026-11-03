namespace MyAccountingApp.AppServices;

/// <summary>
/// Settings → Inventory switch, saved in Preferences. On: a sale / purchase return may take more than the stock on
/// hand (the user is asked first). Off: confirming such an order is refused.
/// </summary>
public static class StockSettings
{
    private const string AllowNegativeKey = "allow_negative_stock";

    public static bool AllowNegative
    {
        get => Preferences.Default.Get(AllowNegativeKey, true);
        set => Preferences.Default.Set(AllowNegativeKey, value);
    }
}

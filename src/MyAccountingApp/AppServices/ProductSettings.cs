namespace MyAccountingApp.AppServices;

/// <summary>Settings → Products switch, saved in Preferences: list inactive products on the Products page.</summary>
public static class ProductSettings
{
    private const string ShowInactiveKey = "show_inactive_products";

    public static bool ShowInactive
    {
        get => Preferences.Default.Get(ShowInactiveKey, false);
        set => Preferences.Default.Set(ShowInactiveKey, value);
    }
}

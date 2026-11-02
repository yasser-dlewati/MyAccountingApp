namespace MyAccountingApp.AppServices;

/// <summary>Settings → General switch, saved in Preferences: ask before Back on the dashboard closes the app.</summary>
public static class ExitSettings
{
    private const string ConfirmKey = "confirm_exit";

    public static bool Confirm
    {
        get => Preferences.Default.Get(ConfirmKey, true);
        set => Preferences.Default.Set(ConfirmKey, value);
    }
}

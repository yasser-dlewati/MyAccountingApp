namespace MyAccountingApp.AppServices;

/// <summary>
/// App theme (system / light / dark), saved in Preferences. All colors use AppThemeBinding,
/// so setting Application.UserAppTheme updates the open pages live — no Shell reload needed.
/// </summary>
public static class ThemeService
{
    private const string PreferenceKey = "theme";

    public static AppTheme CurrentTheme =>
        (AppTheme)Preferences.Default.Get(PreferenceKey, (int)AppTheme.Unspecified);

    /// <summary>Applies the saved theme; call once the Application exists.</summary>
    public static void Initialize() => Apply(CurrentTheme);

    public static void SetTheme(AppTheme theme)
    {
        Preferences.Default.Set(PreferenceKey, (int)theme);
        Apply(theme);
    }

    private static void Apply(AppTheme theme)
    {
        if (Application.Current is not null)
            Application.Current.UserAppTheme = theme;
    }
}

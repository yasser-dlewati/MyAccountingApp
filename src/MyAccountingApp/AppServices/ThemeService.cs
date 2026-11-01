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

#if ANDROID
        // Android 12+ also keeps it as the app's night mode, so the system splash screen
        // (drawn before the app runs) uses the matching light/dark resources on the next launch.
        if (OperatingSystem.IsAndroidVersionAtLeast(31)
            && Platform.AppContext.GetSystemService(Android.Content.Context.UiModeService) is Android.App.UiModeManager uiMode)
            uiMode.SetApplicationNightMode(theme switch
            {
                AppTheme.Light => (int)Android.App.UiNightMode.No,
                AppTheme.Dark => (int)Android.App.UiNightMode.Yes,
                _ => (int)Android.App.UiNightMode.Auto,
            });
#endif
    }
}

namespace MyAccountingApp.Controls.Charts;

/// <summary>
/// Chart colors, picked for the current theme when read (charts redraw on a theme change; the dashboard rebuilds
/// its slices on appearing). Categorical slots are in a fixed, colorblind-validated order — assign them in order,
/// never cycle: a 6th+ entry folds into "Other" (<see cref="Other"/>).
/// </summary>
public static class ChartPalette
{
    private static readonly Color[] Light = [.. new[] { "#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4" }.Select(Color.FromArgb)];
    private static readonly Color[] Dark = [.. new[] { "#3987e5", "#d95926", "#199e70", "#c98500", "#d55181" }.Select(Color.FromArgb)];

    public const int SlotCount = 5;

    private static bool IsDark => Application.Current?.RequestedTheme == AppTheme.Dark;

    public static Color Series(int slot) => (IsDark ? Dark : Light)[slot % SlotCount];

    public static Color Other => Color.FromArgb(IsDark ? "#6E6E6E" : "#ACACAC");

    public static Color Negative => Color.FromArgb(IsDark ? "#e66767" : "#e34948");

    public static Color Grid => Color.FromArgb(IsDark ? "#404040" : "#E1E1E1");

    public static Color Text => Color.FromArgb(IsDark ? "#ACACAC" : "#6E6E6E");

    public static Color Surface => Color.FromArgb(IsDark ? "#212121" : "#FFFFFF");
}

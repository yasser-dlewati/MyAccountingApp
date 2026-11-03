#if ANDROID
using Android.Content.Res;
using Android.Graphics.Drawables;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using AColor = Android.Graphics.Color;
using AView = Android.Views.View;
#endif

namespace MyAccountingApp.Controls;

/// <summary>
/// Text inputs (Entry, Editor, Picker, DatePicker, TimePicker, SearchBar) drawn as rounded, outlined boxes instead of
/// Android's underline: a filled background, a gray 1dp border that turns green (Primary) while focused, and a
/// faded look when disabled. Applied app-wide by handler mapping, so XAML needs no changes.
/// Re-applied on TextColor, which every input style binds with AppThemeBinding, so a theme switch recolors them.
/// </summary>
public static class RoundedInputs
{
    public static void Register()
    {
#if ANDROID
        // Background too: MAUI's own Background mapping would otherwise replace the drawable when it runs later.
        foreach (var key in new[] { nameof(IView.Background), nameof(ITextStyle.TextColor) })
        {
            EntryHandler.Mapper.AppendToMapping(key, (h, _) => Apply(h.PlatformView));
            EditorHandler.Mapper.AppendToMapping(key, (h, _) => Apply(h.PlatformView));
            PickerHandler.Mapper.AppendToMapping(key, (h, _) => Apply(h.PlatformView));
            DatePickerHandler.Mapper.AppendToMapping(key, (h, _) => Apply(h.PlatformView));
            TimePickerHandler.Mapper.AppendToMapping(key, (h, _) => Apply(h.PlatformView));
            SearchBarHandler.Mapper.AppendToMapping(key, (h, _) => ApplySearch(h.PlatformView));
        }
#endif
    }

#if ANDROID
    private const float CornerDp = 10;
    private const int StrokeDp = 1;

    private static void Apply(AView view)
    {
        view.Background = CreateBackground(view);
        var h = Px(view, 12);
        var v = Px(view, 8);
        view.SetPadding(h, v, h, v);
    }

    /// <summary>SearchView: round the whole bar and drop the inner underline ("search_plate").</summary>
    private static void ApplySearch(AView view)
    {
        view.Background = CreateBackground(view);
        var plateId = view.Resources?.GetIdentifier("search_plate", "id", view.Context?.PackageName) ?? 0;
        if (plateId != 0 && view.FindViewById(plateId) is { } plate)
            plate.Background = new ColorDrawable(AColor.Transparent);
    }

    private static GradientDrawable CreateBackground(AView view)
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var primary = ((Color)Application.Current!.Resources["Primary"]).ToPlatform();
        var border = AColor.ParseColor(dark ? "#5C5C5C" : "#BDBDBD");
        var fill = AColor.ParseColor(dark ? "#262626" : "#FFFFFF");
        var disabledFill = AColor.ParseColor(dark ? "#1E1E1E" : "#F2F2F2");
        var disabledBorder = AColor.ParseColor(dark ? "#3A3A3A" : "#E0E0E0");

        int[][] states =
        [
            [-Android.Resource.Attribute.StateEnabled],
            [Android.Resource.Attribute.StateFocused],
            []
        ];

        var drawable = new GradientDrawable();
        drawable.SetShape(ShapeType.Rectangle);
        drawable.SetCornerRadius(Px(view, CornerDp));
        drawable.SetColor(new ColorStateList(states, [disabledFill, fill, fill]));
        drawable.SetStroke(Px(view, StrokeDp), new ColorStateList(states, [disabledBorder, primary, border]));
        return drawable;
    }

    private static int Px(AView view, float dp) =>
        (int)Math.Round(dp * (view.Resources?.DisplayMetrics?.Density ?? 1));
#endif
}

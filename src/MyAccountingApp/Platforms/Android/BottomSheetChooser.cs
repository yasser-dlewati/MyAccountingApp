using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Views;
using Android.Widget;
using Google.Android.Material.BottomSheet;
using Microsoft.Maui.Platform;
using AColor = Android.Graphics.Color;
using AView = Android.Views.View;

namespace MyAccountingApp.AppServices;

/// <summary>
/// Modern replacement for the centered action-sheet dialog (<see cref="DialogService.ChooseAsync"/>): a Material bottom
/// sheet with a drag handle, a title, one 52dp row per option and a Cancel row. Swipe down / tap outside / Back = cancel.
/// </summary>
internal static class BottomSheetChooser
{
    public static Task<string?> ShowAsync(string title, string cancel, IReadOnlyList<string> options)
    {
        var activity = Platform.CurrentActivity;
        if (activity is null) return Task.FromResult<string?>(null);

        var tcs = new TaskCompletionSource<string?>();
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var rtl = Localization.LocalizationService.IsRightToLeft;
        var text = AColor.ParseColor(dark ? "#E1E1E1" : "#212121");
        var muted = AColor.ParseColor(dark ? "#ACACAC" : "#6E6E6E");
        int Px(float dp) => (int)Math.Round(dp * (activity.Resources?.DisplayMetrics?.Density ?? 1));

        var dialog = new BottomSheetDialog(activity);
        var root = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        root.LayoutDirection = rtl ? Android.Views.LayoutDirection.Rtl : Android.Views.LayoutDirection.Ltr;
        root.SetPadding(0, Px(10), 0, Px(12));
        var sheetBg = new GradientDrawable();
        var r = Px(20);
        sheetBg.SetCornerRadii([r, r, r, r, 0, 0, 0, 0]);
        sheetBg.SetColor(AColor.ParseColor(dark ? "#212121" : "#FFFFFF"));
        root.Background = sheetBg;

        // Drag handle
        var handle = new AView(activity);
        var handleBg = new GradientDrawable();
        handleBg.SetCornerRadius(Px(2));
        handleBg.SetColor(AColor.ParseColor(dark ? "#5C5C5C" : "#C8C8C8"));
        handle.Background = handleBg;
        root.AddView(handle, new LinearLayout.LayoutParams(Px(36), Px(4)) { Gravity = GravityFlags.CenterHorizontal, BottomMargin = Px(10) });

        if (!string.IsNullOrWhiteSpace(title))
        {
            var header = new TextView(activity) { Text = title, TextSize = 14 };
            header.SetTextColor(muted);
            header.SetTypeface(Typeface.Default, TypefaceStyle.Bold);
            header.SetPaddingRelative(Px(24), Px(4), Px(24), Px(8));
            header.TextDirection = rtl ? TextDirection.Rtl : TextDirection.Ltr;
            root.AddView(header);
        }

        string? result = null;
        TextView Row(string label, AColor color, bool bold)
        {
            var row = new TextView(activity) { Text = label, TextSize = 16, Gravity = GravityFlags.CenterVertical | GravityFlags.Start };
            row.SetTextColor(color);
            row.SetSingleLine(true);
            row.Ellipsize = TextUtils.TruncateAt.End;
            row.SetMinHeight(Px(52));
            row.SetPaddingRelative(Px(24), 0, Px(24), 0);
            row.TextDirection = rtl ? TextDirection.Rtl : TextDirection.Ltr;
            if (bold) row.SetTypeface(Typeface.Default, TypefaceStyle.Bold);
            var states = new StateListDrawable();
            states.AddState([Android.Resource.Attribute.StatePressed], new ColorDrawable(AColor.ParseColor(dark ? "#333333" : "#F2F2F2")));
            states.AddState([], new ColorDrawable(AColor.Transparent));
            row.Background = states;
            row.Clickable = true;
            return row;
        }

        var list = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        foreach (var option in options)
        {
            var row = Row(option, text, false);
            row.Click += (_, _) => { result = option; dialog.Dismiss(); };
            list.AddView(row);
        }
        var scroll = new Android.Widget.ScrollView(activity);
        scroll.AddView(list);
        root.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));

        var divider = new AView(activity);
        divider.SetBackgroundColor(AColor.ParseColor(dark ? "#404040" : "#E1E1E1"));
        root.AddView(divider, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Px(1)) { TopMargin = Px(4), BottomMargin = Px(4) });
        var cancelRow = Row(cancel, AColor.ParseColor(dark ? "#EF9A9A" : "#C62828"), true);
        cancelRow.Click += (_, _) => dialog.Dismiss();
        root.AddView(cancelRow);

        dialog.SetContentView(root);
        dialog.DismissEvent += (_, _) => tcs.TrySetResult(result);
        dialog.ShowEvent += (_, _) =>
        {
            // Transparent container so the rounded top corners show; open fully expanded.
            if (dialog.FindViewById(Resource.Id.design_bottom_sheet) is { } sheet)
                sheet.SetBackgroundColor(AColor.Transparent);
            dialog.Behavior.State = BottomSheetBehavior.StateExpanded;
            dialog.Behavior.SkipCollapsed = true;
        };
        dialog.Show();
        return tcs.Task;
    }
}

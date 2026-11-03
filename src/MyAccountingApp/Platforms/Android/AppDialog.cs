using ColorStateList = Android.Content.Res.ColorStateList;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Views;
using Android.Widget;
using AColor = Android.Graphics.Color;
using AView = Android.Views.View;

namespace MyAccountingApp.AppServices;

/// <summary>
/// Replacement for the stock alert dialog (<see cref="DialogService"/>): a rounded card with a colored icon badge, a bold
/// title, the message, and the buttons side by side (50 / 50) — cancel on the start side (neutral gray), accept on the
/// end side (green, or red for <see cref="DialogTone.Danger"/>). With no cancel text the accept button fills the row.
/// Back / tap outside = cancel.
/// </summary>
internal static class AppDialog
{
    public static Task<bool> ShowAsync(string title, string message, string accept, string? cancel, DialogTone tone)
    {
        var activity = Platform.CurrentActivity;
        if (activity is null) return Task.FromResult(false);

        var tcs = new TaskCompletionSource<bool>();
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var rtl = Localization.LocalizationService.IsRightToLeft;
        var density = activity.Resources?.DisplayMetrics?.Density ?? 1;
        int Px(float dp) => (int)Math.Round(dp * density);

        var danger = tone == DialogTone.Danger;
        var accent = AColor.ParseColor(danger ? "#C62828" : "#2E7D32");
        var accentText = AColor.ParseColor(danger ? (dark ? "#EF9A9A" : "#C62828") : (dark ? "#81C784" : "#2E7D32"));
        var badgeBg = AColor.ParseColor(danger ? (dark ? "#3B1F1F" : "#FDECEA") : (dark ? "#1B3A1D" : "#E8F5E9"));

        var dialog = new Android.App.Dialog(activity);
        dialog.RequestWindowFeature((int)WindowFeatures.NoTitle);

        var root = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        root.LayoutDirection = rtl ? Android.Views.LayoutDirection.Rtl : Android.Views.LayoutDirection.Ltr;
        root.SetPadding(Px(24), Px(24), Px(24), Px(20));
        var cardBg = new GradientDrawable();
        cardBg.SetCornerRadius(Px(24));
        cardBg.SetColor(AColor.ParseColor(dark ? "#262626" : "#FFFFFF"));
        root.Background = cardBg;

        // Icon badge: "!" for danger, "?" for a question, "i" for a plain message.
        var badge = new TextView(activity)
        {
            Text = danger ? "!" : cancel is null ? "i" : "?",
            TextSize = 22,
            Gravity = GravityFlags.Center
        };
        badge.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        badge.SetTextColor(accentText);
        var badgeShape = new GradientDrawable();
        badgeShape.SetShape(ShapeType.Oval);
        badgeShape.SetColor(badgeBg);
        badge.Background = badgeShape;
        root.AddView(badge, new LinearLayout.LayoutParams(Px(48), Px(48)) { Gravity = GravityFlags.CenterHorizontal, BottomMargin = Px(14) });

        var header = new TextView(activity) { Text = title, TextSize = 19, Gravity = GravityFlags.Center };
        header.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        header.SetTextColor(AColor.ParseColor(dark ? "#F2F2F2" : "#1C1C1C"));
        header.TextDirection = rtl ? TextDirection.Rtl : TextDirection.Ltr;
        root.AddView(header, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        if (!string.IsNullOrWhiteSpace(message))
        {
            var body = new TextView(activity) { Text = message, TextSize = 15, Gravity = GravityFlags.Center };
            body.SetTextColor(AColor.ParseColor(dark ? "#BDBDBD" : "#5F5F5F"));
            body.SetLineSpacing(Px(3), 1f);
            body.TextDirection = rtl ? TextDirection.Rtl : TextDirection.Ltr;
            // Long messages (product details, below-cost lines) scroll instead of pushing the buttons off screen.
            var scroll = new Android.Widget.ScrollView(activity) { VerticalScrollBarEnabled = false };
            scroll.AddView(body);
            root.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, 1f)
                { TopMargin = Px(8) });
        }

        var result = false;
        Android.Widget.Button MakeButton(string label, AColor bg, AColor fg)
        {
            var button = new Android.Widget.Button(activity) { Text = label, TextSize = 15, StateListAnimator = null };
            button.SetAllCaps(false);
            button.SetSingleLine(true);
            button.Ellipsize = TextUtils.TruncateAt.End;
            button.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
            button.SetTextColor(fg);
            button.SetMinHeight(Px(48));
            button.SetMinimumHeight(Px(48));
            button.SetPadding(Px(12), 0, Px(12), 0);
            var shape = new GradientDrawable();
            shape.SetCornerRadius(Px(14));
            shape.SetColor(bg);
            var mask = new GradientDrawable();
            mask.SetCornerRadius(Px(14));
            mask.SetColor(AColor.White);
            button.Background = new RippleDrawable(
                ColorStateList.ValueOf(AColor.ParseColor(dark ? "#33FFFFFF" : "#22000000")), shape, mask);
            return button;
        }

        var row = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
        if (cancel is not null)
        {
            var no = MakeButton(cancel,
                AColor.ParseColor(dark ? "#3A3A3A" : "#EEEEEE"), AColor.ParseColor(dark ? "#E6E6E6" : "#424242"));
            no.Click += (_, _) => dialog.Dismiss();
            row.AddView(no, new LinearLayout.LayoutParams(0, Px(48), 1f) { MarginEnd = Px(6) });
        }
        var yes = MakeButton(accept, accent, AColor.White);
        yes.Click += (_, _) => { result = true; dialog.Dismiss(); };
        row.AddView(yes, new LinearLayout.LayoutParams(0, Px(48), 1f) { MarginStart = cancel is null ? 0 : Px(6) });
        root.AddView(row, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
            { TopMargin = Px(22) });

        dialog.SetContentView(root);
        dialog.SetCancelable(true);
        dialog.SetCanceledOnTouchOutside(true);
        dialog.DismissEvent += (_, _) => tcs.TrySetResult(result);

        dialog.Window?.SetBackgroundDrawable(new ColorDrawable(AColor.Transparent));
        dialog.Show();
        // After Show: the window only takes its size once it is attached.
        if (dialog.Window is { } window)
        {
            var screen = activity.Resources?.DisplayMetrics?.WidthPixels ?? Px(360);
            window.SetLayout(Math.Min(screen - Px(48), Px(400)), ViewGroup.LayoutParams.WrapContent);
            window.SetDimAmount(0.5f);
        }
        return tcs.Task;
    }
}

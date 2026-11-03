using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Views;
using Android.Widget;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using AColor = Android.Graphics.Color;
using APaint = Android.Graphics.Paint;
using AView = Android.Views.View;

namespace MyAccountingApp.Controls;

/// <summary>
/// Picker that opens a dropdown list anchored under the field (Android <see cref="ListPopupWindow"/>) instead of MAUI's
/// centered dialog. MAUI opens its dialog from the view's click listener (its Focus / IsOpen mappings call
/// <c>CallOnClick</c> too), and an Android view has a single click listener, so replacing it reroutes every open here.
/// Registered for <see cref="Picker"/> in <c>MauiProgram</c>; the rounded box + chevron come from <see cref="RoundedInputs"/>.
/// </summary>
public class DropdownPickerHandler : PickerHandler
{
    private ListPopupWindow? _popup;

    protected override void ConnectHandler(MauiPicker platformView)
    {
        base.ConnectHandler(platformView);
        platformView.SetOnClickListener(new ClickListener(this));
    }

    protected override void DisconnectHandler(MauiPicker platformView)
    {
        _popup?.Dismiss();
        _popup = null;
        base.DisconnectHandler(platformView);
    }

    private void Show()
    {
        if (_popup is not null || VirtualView is not { IsEnabled: true } picker || PlatformView is not { } anchor) return;
        var count = picker.GetCount();
        if (count == 0) return;

        var context = anchor.Context!;
        var items = Enumerable.Range(0, count).Select(picker.GetItem).ToList();
        var rtl = LocalizationIsRtl(anchor);
        var popup = new ListPopupWindow(context)
        {
            AnchorView = anchor,
            Width = anchor.Width,
            Modal = true,
        };
        popup.SetAdapter(new ItemsAdapter(context, items, picker.SelectedIndex, rtl));
        popup.SetBackgroundDrawable(PopupBackground(anchor));
        popup.VerticalOffset = Px(anchor, 4);
        var rowHeight = Px(anchor, ItemsAdapter.RowDp);
        if (count > 6) popup.Height = (int)(rowHeight * 5.5) + Px(anchor, 12);
        popup.ItemClick += (_, e) =>
        {
            if (VirtualView is { } v) v.SelectedIndex = e.Position;
            popup.Dismiss();
        };
        popup.DismissEvent += (_, _) =>
        {
            _popup = null;
            if (VirtualView is { } v)
            {
                v.IsOpen = false;
                v.IsFocused = false;
            }
            RoundedInputs.SetChevronOpen(anchor, false);
        };

        _popup = popup;
        RoundedInputs.SetChevronOpen(anchor, true);
        popup.Show();
        if (popup.ListView is { } list)
        {
            list.Divider = null;
            list.LayoutDirection = rtl ? Android.Views.LayoutDirection.Rtl : Android.Views.LayoutDirection.Ltr;
            list.SetPadding(0, Px(anchor, 6), 0, Px(anchor, 6));
            list.SetClipToPadding(false);
            if (picker.SelectedIndex > 2) list.SetSelection(picker.SelectedIndex - 2);
        }
        picker.IsOpen = true;
    }

    private static bool LocalizationIsRtl(AView view) =>
        Localization.LocalizationService.IsRightToLeft || view.LayoutDirection == Android.Views.LayoutDirection.Rtl;

    private static GradientDrawable PopupBackground(AView view)
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var d = new GradientDrawable();
        d.SetShape(ShapeType.Rectangle);
        d.SetCornerRadius(Px(view, 12));
        d.SetColor(AColor.ParseColor(dark ? "#2A2A2A" : "#FFFFFF"));
        d.SetStroke(Px(view, 1), AColor.ParseColor(dark ? "#404040" : "#E1E1E1"));
        return d;
    }

    internal static int Px(AView view, float dp) =>
        (int)Math.Round(dp * (view.Resources?.DisplayMetrics?.Density ?? 1));

    private sealed class ClickListener(DropdownPickerHandler handler) : Java.Lang.Object, AView.IOnClickListener
    {
        public void OnClick(AView? v) => handler.Show();
    }

    /// <summary>48dp rows; the selected one is green, semibold, tinted, with a check at the end.</summary>
    private sealed class ItemsAdapter(Context context, IReadOnlyList<string> items, int selected, bool rtl) : BaseAdapter
    {
        public const float RowDp = 48;

        public override int Count => items.Count;
        public override Java.Lang.Object? GetItem(int position) => new Java.Lang.String(items[position]);
        public override long GetItemId(int position) => position;

        public override AView GetView(int position, AView? convertView, ViewGroup? parent)
        {
            var row = convertView as TextView ?? new TextView(context);
            var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
            var primary = AColor.ParseColor(dark ? "#81C784" : "#2E7D32");
            var isSelected = position == selected;

            row.Text = items[position];
            row.SetSingleLine(true);
            row.Ellipsize = TextUtils.TruncateAt.End;
            row.TextSize = 15;
            row.Gravity = GravityFlags.CenterVertical | GravityFlags.Start;
            row.LayoutDirection = rtl ? Android.Views.LayoutDirection.Rtl : Android.Views.LayoutDirection.Ltr;
            row.TextDirection = rtl ? TextDirection.Rtl : TextDirection.Ltr;
            row.SetMinHeight(Px(row, RowDp));
            row.SetPaddingRelative(Px(row, 16), 0, Px(row, 16), 0);
            row.SetTextColor(isSelected ? primary : AColor.ParseColor(dark ? "#E1E1E1" : "#212121"));
            row.SetTypeface(Typeface.Default, isSelected ? TypefaceStyle.Bold : TypefaceStyle.Normal);

            var pressed = AColor.ParseColor(dark ? "#3A3A3A" : "#F2F2F2");
            var tint = AColor.ParseColor(dark ? "#1B3A1D" : "#E8F5E9");
            var states = new StateListDrawable();
            states.AddState([Android.Resource.Attribute.StatePressed], new ColorDrawable(pressed));
            states.AddState([], new ColorDrawable(isSelected ? tint : AColor.Transparent));
            row.Background = states;

            row.SetCompoundDrawablesRelativeWithIntrinsicBounds(null, null, isSelected ? Check(row, primary) : null, null);
            row.CompoundDrawablePadding = Px(row, 8);
            return row;
        }

        private static Drawable? Check(AView view, AColor color)
        {
            // Material "check" icon drawn as a vector path.
            var size = Px(view, 20);
            var bitmap = Bitmap.CreateBitmap(size, size, Bitmap.Config.Argb8888!)!;
            using var canvas = new Canvas(bitmap);
            using var paint = new APaint(PaintFlags.AntiAlias) { Color = color, StrokeWidth = size / 10f, StrokeCap = APaint.Cap.Round, StrokeJoin = APaint.Join.Round };
            paint.SetStyle(APaint.Style.Stroke);
            using var path = new Android.Graphics.Path();
            path.MoveTo(size * 0.2f, size * 0.52f);
            path.LineTo(size * 0.42f, size * 0.74f);
            path.LineTo(size * 0.82f, size * 0.3f);
            canvas.DrawPath(path, paint);
            return new BitmapDrawable(view.Resources, bitmap);
        }
    }
}

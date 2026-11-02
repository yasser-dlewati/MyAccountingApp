using MyAccountingApp.Localization;

namespace MyAccountingApp.Controls.Charts;

/// <summary>One bar: x-axis label (may be empty to skip it), value and the text shown when the bar is tapped.</summary>
public record ChartPoint(string Label, double Value, string Detail);

/// <summary>
/// Single-series bar chart over time (sales / purchases / profit per hour, day or month).
/// Negative values hang below the zero line in <see cref="ChartPalette.Negative"/>. Tapping a bar shows its
/// <see cref="ChartPoint.Detail"/>; otherwise the top line shows the largest value. Mirrored in Arabic.
/// </summary>
public class TrendChart : GraphicsView, IDrawable
{
    public static readonly BindableProperty PointsProperty = BindableProperty.Create(
        nameof(Points), typeof(IReadOnlyList<ChartPoint>), typeof(TrendChart), null,
        propertyChanged: (b, _, _) => ((TrendChart)b).Reset());

    /// <summary>Categorical palette slot of the bars.</summary>
    public static readonly BindableProperty SlotProperty = BindableProperty.Create(
        nameof(Slot), typeof(int), typeof(TrendChart), 0,
        propertyChanged: (b, _, _) => ((TrendChart)b).Invalidate());

    public IReadOnlyList<ChartPoint>? Points
    {
        get => (IReadOnlyList<ChartPoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public int Slot
    {
        get => (int)GetValue(SlotProperty);
        set => SetValue(SlotProperty, value);
    }

    private const float TopBand = 22, BottomBand = 18, BarGap = 2;
    private int _selected = -1;

    public TrendChart()
    {
        Drawable = this;
        HeightRequest = 170;
        // In Arabic the inherited RTL flow mirrors the whole canvas, text included (reads backwards);
        // keep the canvas LTR and place things for RTL ourselves.
        FlowDirection = FlowDirection.LeftToRight;
        StartInteraction += OnTouch;
        Loaded += (_, _) => Application.Current!.RequestedThemeChanged += OnThemeChanged;
        Unloaded += (_, _) => Application.Current!.RequestedThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e) => Invalidate();

    private void Reset()
    {
        _selected = -1;
        Invalidate();
    }

    private static bool Rtl => LocalizationService.IsRightToLeft;

    private void OnTouch(object? sender, TouchEventArgs e)
    {
        var points = Points;
        if (points is not { Count: > 0 } || e.Touches.Length == 0 || Width <= 0) return;
        var x = e.Touches[0].X;
        if (Rtl) x = (float)Width - x;
        var index = Math.Clamp((int)(x / (Width / points.Count)), 0, points.Count - 1);
        _selected = index == _selected ? -1 : index;
        Invalidate();
    }

    public void Draw(ICanvas canvas, RectF rect)
    {
        var points = Points;
        if (points is not { Count: > 0 }) return;

        var max = Math.Max(points.Max(p => p.Value), 0);
        var min = Math.Min(points.Min(p => p.Value), 0);
        var span = max - min;
        var plotTop = rect.Top + TopBand;
        var plotHeight = rect.Height - TopBand - BottomBand;
        var zeroY = span == 0 ? plotTop + plotHeight : plotTop + (float)(max / span) * plotHeight;
        var slotWidth = rect.Width / points.Count;
        var barWidth = Math.Max(slotWidth - BarGap, 1);
        float XOf(int i) => Rtl ? rect.Right - (i + 1) * slotWidth : rect.Left + i * slotWidth;

        canvas.FontSize = 11;
        canvas.FontColor = ChartPalette.Text;

        // Hairline zero / baseline.
        canvas.StrokeColor = ChartPalette.Grid;
        canvas.StrokeSize = 1;
        canvas.DrawLine(rect.Left, zeroY, rect.Right, zeroY);

        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var x = XOf(i) + BarGap / 2;
            if (p.Value != 0 && span != 0)
            {
                var h = Math.Max((float)(Math.Abs(p.Value) / span) * plotHeight, 2);
                var y = p.Value > 0 ? zeroY - h : zeroY;
                canvas.FillColor = p.Value > 0 ? ChartPalette.Series(Slot) : ChartPalette.Negative;
                if (_selected >= 0 && _selected != i) canvas.Alpha = 0.4f;
                var radius = Math.Min(4, barWidth / 2);
                // Rounded on the data end only, square on the zero line.
                if (p.Value > 0)
                    canvas.FillRoundedRectangle(x, y, barWidth, h, radius, radius, 0, 0);
                else
                    canvas.FillRoundedRectangle(x, y, barWidth, h, 0, 0, radius, radius);
                canvas.Alpha = 1;
            }

            if (p.Label.Length > 0)
                canvas.DrawString(p.Label, XOf(i) + slotWidth / 2 - 30, rect.Bottom - BottomBand + 2, 60, BottomBand - 2,
                    HorizontalAlignment.Center, VerticalAlignment.Center);
        }

        // Top line: the tapped bar, else the peak.
        var detail = _selected >= 0 && _selected < points.Count
            ? points[_selected].Detail
            : points.MaxBy(p => p.Value)!.Value > 0 ? "▲ " + points.MaxBy(p => p.Value)!.Detail : "";
        canvas.DrawString(detail, rect.Left, rect.Top, rect.Width, TopBand - 4,
            Rtl ? HorizontalAlignment.Right : HorizontalAlignment.Left, VerticalAlignment.Center);
    }
}

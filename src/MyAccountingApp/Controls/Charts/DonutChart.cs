namespace MyAccountingApp.Controls.Charts;

/// <summary>One donut segment; <see cref="Color"/> also paints its legend swatch.</summary>
public record ChartSlice(string Label, double Value, string ValueText, string Percent, Color Color);

/// <summary>
/// Part-to-whole donut (≤ 6 segments: top 5 + "Other"). Names and values go in a legend next to it
/// (laid out in XAML, so Arabic text is shaped and wrapped normally); the center shows <see cref="CenterText"/>.
/// </summary>
public class DonutChart : GraphicsView, IDrawable
{
    public static readonly BindableProperty SlicesProperty = BindableProperty.Create(
        nameof(Slices), typeof(IReadOnlyList<ChartSlice>), typeof(DonutChart), null,
        propertyChanged: (b, _, _) => ((DonutChart)b).Invalidate());

    public static readonly BindableProperty CenterTextProperty = BindableProperty.Create(
        nameof(CenterText), typeof(string), typeof(DonutChart), null,
        propertyChanged: (b, _, _) => ((DonutChart)b).Invalidate());

    public IReadOnlyList<ChartSlice>? Slices
    {
        get => (IReadOnlyList<ChartSlice>?)GetValue(SlicesProperty);
        set => SetValue(SlicesProperty, value);
    }

    public string? CenterText
    {
        get => (string?)GetValue(CenterTextProperty);
        set => SetValue(CenterTextProperty, value);
    }

    private const float Thickness = 18, GapDegrees = 1.5f;

    public DonutChart()
    {
        Drawable = this;
        WidthRequest = HeightRequest = 120;
        // In Arabic the inherited RTL flow mirrors the whole canvas, text included (reads backwards);
        // keep the canvas LTR and place things for RTL ourselves.
        FlowDirection = FlowDirection.LeftToRight;
        Loaded += (_, _) => Application.Current!.RequestedThemeChanged += OnThemeChanged;
        Unloaded += (_, _) => Application.Current!.RequestedThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e) => Invalidate();

    public void Draw(ICanvas canvas, RectF rect)
    {
        var size = Math.Min(rect.Width, rect.Height) - Thickness;
        var arc = new RectF(rect.Center.X - size / 2, rect.Center.Y - size / 2, size, size);
        var slices = Slices?.Where(s => s.Value > 0).ToList() ?? [];
        var total = slices.Sum(s => s.Value);

        canvas.StrokeSize = Thickness;
        canvas.StrokeLineCap = LineCap.Butt;
        if (total <= 0)
        {
            canvas.StrokeColor = ChartPalette.Grid;
            canvas.DrawEllipse(arc);
        }
        else
        {
            // Clockwise from 12 o'clock; MAUI angles are counter-clockwise from 3 o'clock.
            var start = 90f;
            foreach (var s in slices)
            {
                var sweep = (float)(s.Value / total * 360);
                var gap = slices.Count > 1 ? Math.Min(GapDegrees, sweep / 2) : 0;
                canvas.StrokeColor = s.Color;
                if (sweep >= 359.9f)
                    canvas.DrawEllipse(arc);
                else
                    canvas.DrawArc(arc.X, arc.Y, arc.Width, arc.Height, start - gap / 2, start - sweep + gap / 2, true, false);
                start -= sweep;
            }
        }

        if (!string.IsNullOrEmpty(CenterText))
        {
            canvas.FontSize = 12;
            canvas.FontColor = ChartPalette.Text;
            canvas.DrawString(CenterText, arc, HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }
}

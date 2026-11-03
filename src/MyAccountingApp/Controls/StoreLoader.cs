using MyAccountingApp.Localization;

namespace MyAccountingApp.Controls;

/// <summary>
/// Busy overlay built from the splash screen's store: the store (Resources/Images/store_loader.svg) on a round badge,
/// a green progress arc spinning around it and a gentle pulse, over a translucent scrim that blocks input.
/// Put it last in a page's root Grid spanning every row and bind <see cref="IsRunning"/> to the VM's IsBusy.
/// It only appears when the work takes longer than <see cref="ShowDelay"/> and then stays at least
/// <see cref="MinVisible"/>, so quick loads don't flicker.
/// </summary>
public class StoreLoader : Grid
{
    public static readonly BindableProperty IsRunningProperty = BindableProperty.Create(
        nameof(IsRunning), typeof(bool), typeof(StoreLoader), false,
        propertyChanged: (b, _, n) => ((StoreLoader)b).OnRunningChanged((bool)n));

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(StoreLoader), LocalizationService.Get("Common_Loading"),
        propertyChanged: (b, _, n) => ((StoreLoader)b)._caption.Text = (string)n);

    public bool IsRunning
    {
        get => (bool)GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    /// <summary>Caption under the badge; defaults to "Loading…".</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan MinVisible = TimeSpan.FromMilliseconds(400);
    private const double BadgeSize = 112;

    private readonly ArcDrawable _arc = new();
    private readonly GraphicsView _arcView;
    private readonly Image _store;
    private readonly Label _caption;
    private readonly VerticalStackLayout _content;
    private int _generation;
    private DateTime _shownAt;
    private bool _shown;

    public StoreLoader()
    {
        IsVisible = false;
        InputTransparent = false;
        ZIndex = 100;
        this.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#99FFFFFF"), Color.FromArgb("#B3000000"));

        // Forced LTR like the charts: an inherited RTL would mirror the canvas (the arc would spin backwards).
        _arcView = new GraphicsView { Drawable = _arc, WidthRequest = BadgeSize, HeightRequest = BadgeSize, FlowDirection = FlowDirection.LeftToRight, InputTransparent = true };
        _store = new Image { Source = "store_loader.png", WidthRequest = 62, HeightRequest = 62, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };

        // Light badge in both themes: the artwork's dark outline needs a light background (as on the splash).
        var badge = new Border
        {
            WidthRequest = BadgeSize - 18, HeightRequest = BadgeSize - 18,
            StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = (BadgeSize - 18) / 2 },
            BackgroundColor = Color.FromArgb("#F1F8E9"),
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
            Shadow = new Shadow { Brush = Brush.Black, Opacity = 0.18f, Radius = 14, Offset = new Point(0, 4) },
            Content = _store
        };

        var ring = new Grid { WidthRequest = BadgeSize, HeightRequest = BadgeSize, HorizontalOptions = LayoutOptions.Center };
        ring.Add(badge);
        ring.Add(_arcView);

        _caption = new Label { Text = Text, FontFamily = "OpenSansSemibold", FontSize = 14, HorizontalOptions = LayoutOptions.Center };
        _caption.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#404040"), Color.FromArgb("#E1E1E1"));

        _content = new VerticalStackLayout
        {
            Spacing = 14, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
            Children = { ring, _caption }
        };
        Add(_content);

        // Swallow taps while shown, so the page under it can't be used mid-operation.
        GestureRecognizers.Add(new TapGestureRecognizer());
        Unloaded += (_, _) => StopAnimations();
    }

    private async void OnRunningChanged(bool running)
    {
        var generation = ++_generation;
        if (running)
        {
            await Task.Delay(ShowDelay);
            if (generation != _generation || _shown) return;
            await ShowAsync();
        }
        else if (_shown)
        {
            var left = MinVisible - (DateTime.UtcNow - _shownAt);
            if (left > TimeSpan.Zero) await Task.Delay(left);
            if (generation != _generation) return;
            await HideAsync();
        }
    }

    private async Task ShowAsync()
    {
        _shown = true;
        _shownAt = DateTime.UtcNow;
        Opacity = 0;
        _content.Scale = 0.9;
        IsVisible = true;
        StartAnimations();
        await Task.WhenAll(this.FadeToAsync(1, 160, Easing.CubicOut), _content.ScaleToAsync(1, 220, Easing.CubicOut));
    }

    private async Task HideAsync()
    {
        _shown = false;
        await this.FadeToAsync(0, 160, Easing.CubicIn);
        if (_shown) return;
        IsVisible = false;
        StopAnimations();
    }

    private void StartAnimations()
    {
        this.Animate("spin", v => { _arc.Angle = (float)v; _arcView.Invalidate(); }, 0, 360, length: 1100, easing: Easing.Linear, repeat: () => _shown);
        var pulse = new Animation
        {
            { 0, 0.5, new Animation(v => _store.Scale = v, 1, 1.08, Easing.SinInOut) },
            { 0.5, 1, new Animation(v => _store.Scale = v, 1.08, 1, Easing.SinInOut) }
        };
        pulse.Commit(this, "pulse", length: 1200, repeat: () => _shown);
    }

    private void StopAnimations()
    {
        this.AbortAnimation("spin");
        this.AbortAnimation("pulse");
        _store.Scale = 1;
    }

    /// <summary>Faint track circle plus a 100° green arc starting at <see cref="Angle"/>.</summary>
    private sealed class ArcDrawable : IDrawable
    {
        public float Angle { get; set; }

        public void Draw(ICanvas canvas, RectF rect)
        {
            const float stroke = 5;
            var r = rect.Inflate(-stroke, -stroke);
            var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
            canvas.StrokeSize = stroke;
            canvas.StrokeLineCap = LineCap.Round;

            canvas.StrokeColor = dark ? Color.FromArgb("#33FFFFFF") : Color.FromArgb("#1F2E7D32");
            canvas.DrawEllipse(r);

            canvas.StrokeColor = dark ? Color.FromArgb("#81C784") : Color.FromArgb("#2E7D32");
            // MAUI angles: degrees counter-clockwise from 3 o'clock; draw clockwise as the angle grows.
            var start = -Angle;
            canvas.DrawArc(r.X, r.Y, r.Width, r.Height, start, start - 100, true, false);
        }
    }
}

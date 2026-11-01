using MyAccountingApp.AppServices;

namespace MyAccountingApp.Controls;

/// <summary>
/// Bottom tab bar shown on the main (Shell root) pages. Set <see cref="Current"/> to the page's Shell route
/// to highlight its tab. The center button runs <see cref="QuickScanService"/>.
/// </summary>
public partial class BottomNavBar : ContentView
{
	public static readonly BindableProperty CurrentProperty = BindableProperty.Create(
		nameof(Current), typeof(string), typeof(BottomNavBar), null,
		propertyChanged: (b, _, _) => ((BottomNavBar)b).UpdateSelection());

	/// <summary>Shell route of the page hosting the bar ("dashboard", "orders", "products", "stock").</summary>
	public string? Current
	{
		get => (string?)GetValue(CurrentProperty);
		set => SetValue(CurrentProperty, value);
	}

	public BottomNavBar()
	{
		InitializeComponent();
		UpdateSelection();
	}

	private IEnumerable<(VerticalStackLayout Tab, string Route)> Tabs =>
	[
		(DashboardTab, "dashboard"), (OrdersTab, "orders"), (ProductsTab, "products"), (StockTab, "stock"),
	];

	private void UpdateSelection()
	{
		var resources = Application.Current!.Resources;
		foreach (var (tab, route) in Tabs)
		{
			var selected = route == Current;
			var light = (Color)resources[selected ? "Primary" : "Gray500"];
			var dark = (Color)resources[selected ? "PrimaryDark" : "Gray300"];
			foreach (var child in tab.Children)
			{
				if (child is Microsoft.Maui.Controls.Shapes.Path icon)
					icon.SetAppTheme<Brush>(Microsoft.Maui.Controls.Shapes.Shape.FillProperty, new SolidColorBrush(light), new SolidColorBrush(dark));
				else if (child is Label label)
				{
					label.SetAppThemeColor(Label.TextColorProperty, light, dark);
					label.FontFamily = selected ? "OpenSansSemibold" : "OpenSansRegular";
				}
			}
		}
	}

	private async void OnTabTapped(object? sender, TappedEventArgs e)
	{
		if (e.Parameter is string route && route != Current)
			await Shell.Current.GoToAsync("//" + route, animate: false);
	}

	private async void OnScanTapped(object? sender, TappedEventArgs e)
	{
		var services = Handler?.MauiContext?.Services ?? IPlatformApplication.Current!.Services;
		await services.GetRequiredService<QuickScanService>().ScanAsync();
	}
}

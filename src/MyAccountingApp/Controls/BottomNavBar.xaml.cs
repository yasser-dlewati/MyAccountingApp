using MyAccountingApp.AppServices;

namespace MyAccountingApp.Controls;

/// <summary>
/// Bottom tab bar shown on the main pages. Set <see cref="Current"/> to the page's tab to highlight it.
/// The center button runs <see cref="QuickScanService"/>.
/// </summary>
public partial class BottomNavBar : ContentView
{
	public static readonly BindableProperty CurrentProperty = BindableProperty.Create(
		nameof(Current), typeof(string), typeof(BottomNavBar), null,
		propertyChanged: (b, _, _) => ((BottomNavBar)b).UpdateSelection());

	/// <summary>Shell route of the page hosting the bar ("dashboard", "orders", "inventory", "more"); the pages
	/// pushed from More use "more" too.</summary>
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

	private IEnumerable<(VerticalStackLayout Tab, string Name)> Tabs =>
	[
		(DashboardTab, "dashboard"), (OrdersTab, "orders"), (InventoryTab, "inventory"), (MoreTab, "more"),
	];

	private void UpdateSelection()
	{
		var resources = Application.Current!.Resources;
		foreach (var (tab, name) in Tabs)
		{
			var selected = name == Current;
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
		if (e.Parameter is not string name) return;
		if (name != Current)
			await Shell.Current.GoToAsync("//" + name, animate: false);
		else if (Shell.Current.Navigation.NavigationStack.Count > 1)
			await Shell.Current.Navigation.PopToRootAsync(animated: false); // e.g. More while on Settings
	}

	private async void OnScanClicked(object? sender, EventArgs e)
	{
		var services = Handler?.MauiContext?.Services ?? IPlatformApplication.Current!.Services;
		await services.GetRequiredService<QuickScanService>().ScanAsync();
	}
}

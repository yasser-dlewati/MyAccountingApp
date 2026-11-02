using MyAccountingApp.AppServices;
using MyAccountingApp.Core.Enums;

namespace MyAccountingApp.Controls;

/// <summary>
/// Bottom tab bar shown on the main pages. Set <see cref="Current"/> to the page's tab to highlight it.
/// The center button runs <see cref="QuickScanService"/>: tap = scan into a new sale, long press = quick menu.
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
		// The quick menu lives in a page-wide overlay while open (see ShowMenu), never in the bar itself.
		Bar.Remove(QuickMenu);
		UpdateSelection();
		Unloaded += (_, _) => HideMenu();
		ScanButton.HandlerChanging += OnScanButtonHandlerChanging;
		ScanButton.HandlerChanged += OnScanButtonHandlerChanged;
	}

	// MAUI has no long-press gesture: use the platform view's long click (it also suppresses the tap).
	private void OnScanButtonHandlerChanged(object? sender, EventArgs e)
	{
#if ANDROID
		if (ScanButton.Handler?.PlatformView is Android.Views.View view)
			view.LongClick += OnScanLongClick;
#endif
	}

	private void OnScanButtonHandlerChanging(object? sender, HandlerChangingEventArgs e)
	{
#if ANDROID
		if (e.OldHandler?.PlatformView is Android.Views.View view)
			view.LongClick -= OnScanLongClick;
#endif
	}

#if ANDROID
	private void OnScanLongClick(object? sender, Android.Views.View.LongClickEventArgs e)
	{
		e.Handled = true;
		try { HapticFeedback.Default.Perform(HapticFeedbackType.LongPress); }
		catch (FeatureNotSupportedException) { }
		ShowMenu();
	}
#endif

	private Grid? _menuOverlay;

	/// <summary>
	/// Shows the quick menu in a transparent overlay over the whole hosting page (its root Grid), so a tap anywhere
	/// outside the buttons — the scan button included — just closes it.
	/// </summary>
	private void ShowMenu()
	{
		if (_menuOverlay is not null || Parent is not Grid host) return;
		var overlay = new Grid { BackgroundColor = Colors.Transparent, ZIndex = 100 };
		overlay.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(HideMenu) });
		Grid.SetRowSpan(overlay, Math.Max(1, host.RowDefinitions.Count));
		Grid.SetColumnSpan(overlay, Math.Max(1, host.ColumnDefinitions.Count));
		overlay.Add(QuickMenu);
		host.Add(overlay);
		_menuOverlay = overlay;
	}

	private void HideMenu()
	{
		if (_menuOverlay is null) return;
		(_menuOverlay.Parent as Grid)?.Remove(_menuOverlay);
		_menuOverlay.Remove(QuickMenu);
		_menuOverlay = null;
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

	private QuickScanService QuickScan =>
		(Handler?.MauiContext?.Services ?? IPlatformApplication.Current!.Services).GetRequiredService<QuickScanService>();

	private async void OnScanClicked(object? sender, EventArgs e) => await QuickScan.ScanSaleAsync();

	private async void OnNewSaleClicked(object? sender, EventArgs e)
	{
		HideMenu();
		await QuickScan.NewOrderAsync(OrderType.Sale);
	}

	private async void OnNewPurchaseClicked(object? sender, EventArgs e)
	{
		HideMenu();
		await QuickScan.NewOrderAsync(OrderType.Purchase);
	}

	private async void OnCheckProductClicked(object? sender, EventArgs e)
	{
		HideMenu();
		await QuickScan.CheckProductAsync();
	}
}

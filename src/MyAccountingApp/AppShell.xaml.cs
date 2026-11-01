using MyAccountingApp.AppServices;
using MyAccountingApp.Localization;
using MyAccountingApp.Views;

namespace MyAccountingApp;

public partial class AppShell : Shell
{
	static AppShell()
	{
		Routing.RegisterRoute(Routes.ContactEdit, typeof(ContactEditPage));
		Routing.RegisterRoute(Routes.ProductEdit, typeof(ProductEditPage));
		Routing.RegisterRoute(Routes.OrderEdit, typeof(OrderEditPage));
	}

	public AppShell()
	{
		InitializeComponent();
		FlowDirection = LocalizationService.FlowDirection;
		CategoriesItem.FlyoutItemIsVisible = CategorySettings.Enabled;
	}

	/// <summary>Shows / hides the Categories page after the Settings switch changes.</summary>
	public static void UpdateCategoriesVisibility()
	{
		if (Current is AppShell shell)
			shell.CategoriesItem.FlyoutItemIsVisible = CategorySettings.Enabled;
	}
}

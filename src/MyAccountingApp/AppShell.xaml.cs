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

	private bool _exitPromptOpen;

	/// <summary>
	/// Back: a pushed page (order / product / contact edit) pops as usual; any other main page goes to the
	/// dashboard; on the dashboard the app closes, after asking when Settings → Confirm before exiting is on.
	/// </summary>
	protected override bool OnBackButtonPressed()
	{
		if (FlyoutIsPresented)
		{
			FlyoutIsPresented = false;
			return true;
		}
		if (Navigation.ModalStack.Count > 0 || CurrentItem?.CurrentItem?.Stack.Count > 1)
			return base.OnBackButtonPressed();

		if (CurrentItem != DashboardItem)
		{
			CurrentItem = DashboardItem;
			return true;
		}

		if (!ExitSettings.Confirm)
			return base.OnBackButtonPressed();
		if (!_exitPromptOpen)
			MainThread.BeginInvokeOnMainThread(AskToExit);
		return true;
	}

	private async void AskToExit()
	{
		_exitPromptOpen = true;
		try
		{
			var dialogs = Handler?.MauiContext?.Services.GetRequiredService<DialogService>() ?? new DialogService();
			if (await dialogs.ConfirmAsync(LocalizationService.Get("Exit_Prompt")))
				Application.Current?.Quit();
		}
		finally
		{
			_exitPromptOpen = false;
		}
	}

	/// <summary>Shows / hides the Categories page after the Settings switch changes.</summary>
	public static void UpdateCategoriesVisibility()
	{
		if (Current is AppShell shell)
			shell.CategoriesItem.FlyoutItemIsVisible = CategorySettings.Enabled;
	}
}

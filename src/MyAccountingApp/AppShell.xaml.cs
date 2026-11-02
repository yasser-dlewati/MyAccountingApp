using Microsoft.Extensions.Logging;
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
		Routing.RegisterRoute(Routes.Contacts, typeof(ContactsPage));
		Routing.RegisterRoute(Routes.Categories, typeof(CategoriesPage));
		Routing.RegisterRoute(Routes.Settings, typeof(SettingsPage));
	}

	public AppShell()
	{
		InitializeComponent();
		FlowDirection = LocalizationService.FlowDirection;
	}

	private bool _exitPromptOpen;

	/// <summary>
	/// Back: a pushed page (order / product / contact edit, the pages opened from More) pops as usual; any other main page goes to the
	/// dashboard; on the dashboard the app closes, after asking when Settings → Confirm before exiting is on.
	/// </summary>
	protected override bool OnBackButtonPressed()
	{
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

	/// <summary>Page breadcrumbs in the log file, so a crash can be traced to the screen it happened on.</summary>
	protected override void OnNavigated(ShellNavigatedEventArgs args)
	{
		base.OnNavigated(args);
		Handler?.MauiContext?.Services.GetService<ILogger<AppShell>>()
			?.LogInformation("Navigated to {Location} ({Source})", args.Current?.Location, args.Source);
	}
}

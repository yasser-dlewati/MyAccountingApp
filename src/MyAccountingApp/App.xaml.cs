namespace MyAccountingApp;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}

	/// <summary>Recreates the Shell after a language change or database restore.</summary>
	public static void ReloadShell()
	{
		var window = Current?.Windows.FirstOrDefault();
		if (window is not null)
			window.Page = new AppShell();
	}
}

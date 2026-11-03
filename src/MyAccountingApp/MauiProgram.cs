using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyAccountingApp.AppServices;
using MyAccountingApp.Core.Data;
using MyAccountingApp.Core.Services;
using MyAccountingApp.Localization;
using MyAccountingApp.ViewModels;
using MyAccountingApp.Views;
using ZXing.Net.Maui.Controls;

namespace MyAccountingApp;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		LocalizationService.Initialize();

		// Rounded, outlined text inputs app-wide (Android handler mapping).
		Controls.RoundedInputs.Register();

		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseBarcodeReader()
			.ConfigureMauiHandlers(handlers =>
			{
#if ANDROID
				// Pickers open a dropdown under the field instead of a centered dialog.
				handlers.AddHandler<Picker, Controls.DropdownPickerHandler>();
#endif
			})
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// Data: one short-lived DbContext per operation.
		builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite($"Data Source={BackupService.DatabasePath}"));

		// Core services are stateless, so singletons are fine.
		builder.Services.AddSingleton<CategoryService>();
		builder.Services.AddSingleton<ContactService>();
		builder.Services.AddSingleton<ProductService>();
		builder.Services.AddSingleton<InventoryService>();
		builder.Services.AddSingleton<OrderService>();
		builder.Services.AddSingleton<ReportService>();
		builder.Services.AddSingleton<DialogService>();
		builder.Services.AddSingleton<BackupService>();
		builder.Services.AddSingleton<BarcodeScannerService>();
		builder.Services.AddSingleton<QuickScanService>();
		builder.Services.AddSingleton<PhoneContactsService>();

		builder.Services.AddTransient<DashboardPage>().AddTransient<DashboardViewModel>();
		builder.Services.AddTransient<OrdersPage>().AddTransient<OrdersViewModel>();
		builder.Services.AddTransient<OrderEditPage>().AddTransient<OrderEditViewModel>();
		builder.Services.AddTransient<ProductsPage>().AddTransient<ProductsViewModel>();
		builder.Services.AddTransient<ProductEditPage>().AddTransient<ProductEditViewModel>();
		builder.Services.AddTransient<CategoriesPage>().AddTransient<CategoriesViewModel>();
		builder.Services.AddTransient<ContactsPage>().AddTransient<ContactsViewModel>();
		builder.Services.AddTransient<ContactEditPage>().AddTransient<ContactEditViewModel>();
		builder.Services.AddTransient<ContactImportPage>().AddTransient<ContactImportViewModel>();
		builder.Services.AddTransient<SettingsPage>().AddTransient<SettingsViewModel>();
		builder.Services.AddTransient<MorePage>().AddTransient<MoreViewModel>();

		// Log files: see FileLoggerProvider.Directory; shared from Settings → Share logs.
		builder.Logging.AddProvider(new FileLoggerProvider());
		builder.Logging.SetMinimumLevel(LogLevel.Information);
		builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
#if DEBUG
		builder.Logging.AddDebug();
		builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Information);
#endif

		var app = builder.Build();

		var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MyAccountingApp.Startup");
		LogUnhandledExceptions(log);
		log.LogInformation("App {Version} ({Build}) starting on {Platform} {OsVersion}, {Manufacturer} {Model}, language {Language}",
			AppInfo.Current.VersionString, AppInfo.Current.BuildString, DeviceInfo.Current.Platform,
			DeviceInfo.Current.VersionString, DeviceInfo.Current.Manufacturer, DeviceInfo.Current.Model,
			LocalizationService.CurrentLanguage);

		using (var db = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext())
		{
			var pending = db.Database.GetPendingMigrations().ToList();
			if (pending.Count > 0)
				log.LogInformation("Applying migrations: {Migrations}", string.Join(", ", pending));
			db.Database.Migrate();
		}

		return app;
	}

	private static void LogUnhandledExceptions(ILogger log)
	{
		AppDomain.CurrentDomain.UnhandledException += (_, e) =>
			log.LogCritical(e.ExceptionObject as Exception, "Unhandled exception (terminating: {Terminating})", e.IsTerminating);
		TaskScheduler.UnobservedTaskException += (_, e) =>
			log.LogError(e.Exception, "Unobserved task exception");
	}
}

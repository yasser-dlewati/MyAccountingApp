using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyAccountingApp.AppServices;
using MyAccountingApp.Core.Data;
using MyAccountingApp.Core.Services;
using MyAccountingApp.Localization;
using MyAccountingApp.ViewModels;
using MyAccountingApp.Views;

namespace MyAccountingApp;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		LocalizationService.Initialize();

		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
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
		builder.Services.AddSingleton<DialogService>();
		builder.Services.AddSingleton<BackupService>();

		builder.Services.AddTransient<DashboardPage>().AddTransient<DashboardViewModel>();
		builder.Services.AddTransient<OrdersPage>().AddTransient<OrdersViewModel>();
		builder.Services.AddTransient<OrderEditPage>().AddTransient<OrderEditViewModel>();
		builder.Services.AddTransient<ProductsPage>().AddTransient<ProductsViewModel>();
		builder.Services.AddTransient<ProductEditPage>().AddTransient<ProductEditViewModel>();
		builder.Services.AddTransient<CategoriesPage>().AddTransient<CategoriesViewModel>();
		builder.Services.AddTransient<ContactsPage>().AddTransient<ContactsViewModel>();
		builder.Services.AddTransient<ContactEditPage>().AddTransient<ContactEditViewModel>();
		builder.Services.AddTransient<StockPage>().AddTransient<StockViewModel>();
		builder.Services.AddTransient<SettingsPage>().AddTransient<SettingsViewModel>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		var app = builder.Build();

		using (var db = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext())
			db.Database.Migrate();

		return app;
	}
}

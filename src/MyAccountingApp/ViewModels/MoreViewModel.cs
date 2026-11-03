using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MyAccountingApp.AppServices;
using MyAccountingApp.Localization;

namespace MyAccountingApp.ViewModels;

/// <summary>The More tab: links to the less-used pages (contacts, categories, settings), share logs and about.</summary>
public partial class MoreViewModel(DialogService dialogs, ILogger<MoreViewModel> log) : BaseViewModel(dialogs)
{
    private const string DeveloperWebsite = "https://yasser-dlewati.github.io/me";

    /// <summary>Settings → Categories; re-read on every appearing since Settings is opened from here.</summary>
    [ObservableProperty]
    public partial bool ShowCategories { get; set; } = CategorySettings.Enabled;

    public string AppVersion => AppInfo.Current.VersionString;

    [RelayCommand]
    private void Load() => ShowCategories = CategorySettings.Enabled;

    [RelayCommand]
    private Task OpenAsync(string route) => GoToAsync(route);

    /// <summary>Tapping the YASDLE logo: a popup with the developer's website, opened in the browser on request.</summary>
    [RelayCommand]
    private Task ContactDeveloperAsync() => RunAsync(async () =>
    {
        if (!await Dialogs.DetailsAsync(LocalizationService.Get("About_ContactDeveloper"),
                LocalizationService.Format("About_ContactDeveloperMessage", DeveloperWebsite),
                LocalizationService.Get("About_OpenWebsite")))
            return;
        log.LogInformation("Opening developer website");
        await Browser.Default.OpenAsync(DeveloperWebsite, BrowserLaunchMode.SystemPreferred);
    });

    /// <summary>Log files are in app-private storage, so they leave the phone through the share sheet.</summary>
    [RelayCommand]
    private Task ShareLogsAsync() => RunAsync(async () =>
    {
        try
        {
            var zip = FileLoggerProvider.CreateArchive();
            await Share.Default.RequestAsync(new ShareFileRequest(LocalizationService.Get("Settings_ShareLogs"), new ShareFile(zip)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogError(ex, "Sharing logs failed");
            await Dialogs.AlertAsync(LocalizationService.Format("Settings_ShareLogsFailed", ex.Message));
        }
    });
}

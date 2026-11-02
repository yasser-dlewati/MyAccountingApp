using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MyAccountingApp.AppServices;
using MyAccountingApp.Helpers;
using MyAccountingApp.Localization;

namespace MyAccountingApp.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private readonly BackupService _backup;
    private readonly ILogger<SettingsViewModel> _log;

    public SettingsViewModel(BackupService backup, DialogService dialogs, ILogger<SettingsViewModel> log) : base(dialogs)
    {
        _backup = backup;
        _log = log;
        Language = LanguageOptions.First(o => o.Value == LocalizationService.CurrentLanguage);
        Theme = ThemeOptions.FirstOrDefault(o => o.Value == ThemeService.CurrentTheme) ?? ThemeOptions[0];
        ConfirmExit = ExitSettings.Confirm;
        ShowInactiveProducts = ProductSettings.ShowInactive;
        UseCategories = CategorySettings.Enabled;
        RequireCustomer = !WalkInSettings.CustomerAllowed;
        RequireSupplier = !WalkInSettings.SupplierAllowed;
    }

    public List<Option<string>> LanguageOptions { get; } =
    [
        new("English", LocalizationService.English),
        new("العربية", LocalizationService.Arabic)
    ];

    [ObservableProperty]
    public partial Option<string> Language { get; set; }

    public List<Option<AppTheme>> ThemeOptions { get; } =
    [
        new(LocalizationService.Get("Theme_System"), AppTheme.Unspecified),
        new(LocalizationService.Get("Theme_Light"), AppTheme.Light),
        new(LocalizationService.Get("Theme_Dark"), AppTheme.Dark)
    ];

    [ObservableProperty]
    public partial Option<AppTheme> Theme { get; set; }

    [ObservableProperty]
    public partial bool ConfirmExit { get; set; }

    [ObservableProperty]
    public partial bool ShowInactiveProducts { get; set; }

    [ObservableProperty]
    public partial bool UseCategories { get; set; }

    [ObservableProperty]
    public partial bool RequireCustomer { get; set; }

    [ObservableProperty]
    public partial bool RequireSupplier { get; set; }

    partial void OnThemeChanged(Option<AppTheme> value)
    {
        if (value.Value == ThemeService.CurrentTheme) return;
        _log.LogInformation("Theme changed to {Theme}", value.Value);
        ThemeService.SetTheme(value.Value);
    }

    partial void OnConfirmExitChanged(bool value) => ExitSettings.Confirm = value;

    partial void OnShowInactiveProductsChanged(bool value) => ProductSettings.ShowInactive = value;

    // The More page shows / hides its Categories row when it next appears.
    partial void OnUseCategoriesChanged(bool value) => CategorySettings.Enabled = value;

    partial void OnRequireCustomerChanged(bool value) => WalkInSettings.CustomerAllowed = !value;

    partial void OnRequireSupplierChanged(bool value) => WalkInSettings.SupplierAllowed = !value;

    partial void OnLanguageChanged(Option<string> value)
    {
        if (value.Value == LocalizationService.CurrentLanguage) return;
        _log.LogInformation("Language changed to {Language}", value.Value);
        LocalizationService.SetLanguage(value.Value);
        // Rebuild the UI so every page picks up the new strings and flow direction.
        MainThread.BeginInvokeOnMainThread(App.ReloadShell);
    }

    [RelayCommand]
    private Task BackupAsync() => RunAsync(async () =>
    {
        try
        {
            await _backup.ShareBackupAsync(LocalizationService.Get("Settings_Backup"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogError(ex, "Backup failed");
            await Dialogs.AlertAsync(LocalizationService.Format("Settings_BackupFailed", ex.Message));
        }
    });

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (!await Dialogs.ConfirmAsync(LocalizationService.Get("Settings_RestorePrompt"))) return;
        await RunAsync(async () =>
        {
            try
            {
                if (!await _backup.RestoreAsync()) return;
                await Dialogs.AlertAsync(LocalizationService.Get("Settings_RestoreDone"));
                App.ReloadShell();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                           or Microsoft.Data.Sqlite.SqliteException)
            {
                _log.LogError(ex, "Restore failed");
                await Dialogs.AlertAsync(LocalizationService.Format("Settings_RestoreFailed", ex.Message));
            }
        });
    }
}

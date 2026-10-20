using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAccountingApp.AppServices;
using MyAccountingApp.Helpers;
using MyAccountingApp.Localization;

namespace MyAccountingApp.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private readonly BackupService _backup;

    public SettingsViewModel(BackupService backup, DialogService dialogs) : base(dialogs)
    {
        _backup = backup;
        Language = LanguageOptions.First(o => o.Value == LocalizationService.CurrentLanguage);
    }

    public List<Option<string>> LanguageOptions { get; } =
    [
        new("English", LocalizationService.English),
        new("العربية", LocalizationService.Arabic)
    ];

    [ObservableProperty]
    public partial Option<string> Language { get; set; }

    public string AppVersion => AppInfo.Current.VersionString;

    partial void OnLanguageChanged(Option<string> value)
    {
        if (value.Value == LocalizationService.CurrentLanguage) return;
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
                await Dialogs.AlertAsync(LocalizationService.Format("Settings_RestoreFailed", ex.Message));
            }
        });
    }
}

using MyAccountingApp.Core.Services;
using MyAccountingApp.Localization;

namespace MyAccountingApp.AppServices;

public class DialogService
{
    private static Page Page => Shell.Current ?? Application.Current!.Windows[0].Page!;

    public Task AlertAsync(string message) =>
        Page.DisplayAlertAsync(LocalizationService.Get("App_Title"), message, LocalizationService.Get("Common_Ok"));

    public Task<bool> ConfirmAsync(string message) =>
        Page.DisplayAlertAsync(LocalizationService.Get("App_Title"), message,
            LocalizationService.Get("Common_Yes"), LocalizationService.Get("Common_No"));

    public Task<string?> PromptAsync(string title, string? initial = null, Keyboard? keyboard = null) =>
        Page.DisplayPromptAsync(title, null, LocalizationService.Get("Common_Ok"), LocalizationService.Get("Common_Cancel"),
            initialValue: initial ?? "", keyboard: keyboard ?? Keyboard.Default);

    /// <summary>Returns the chosen label, or null when cancelled.</summary>
    public async Task<string?> ChooseAsync(string title, params string[] options)
    {
        var cancel = LocalizationService.Get("Common_Cancel");
        var choice = await Page.DisplayActionSheetAsync(title, cancel, null, options);
        return choice == cancel ? null : choice;
    }

    public Task ShowErrorAsync(BusinessRuleException ex) =>
        AlertAsync(LocalizationService.Format(ex.ErrorKey, ex.Args));
}

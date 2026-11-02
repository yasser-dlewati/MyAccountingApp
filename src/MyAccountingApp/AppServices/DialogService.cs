using Microsoft.Extensions.Logging;
using MyAccountingApp.Core.Services;
using MyAccountingApp.Localization;

namespace MyAccountingApp.AppServices;

public class DialogService(ILogger<DialogService>? logger = null)
{
    private static Page Page => Shell.Current ?? Application.Current!.Windows[0].Page!;

    public Task AlertAsync(string message) =>
        Page.DisplayAlertAsync(LocalizationService.Get("App_Title"), message, LocalizationService.Get("Common_Ok"));

    public Task<bool> ConfirmAsync(string message) =>
        Page.DisplayAlertAsync(LocalizationService.Get("App_Title"), message,
            LocalizationService.Get("Common_Yes"), LocalizationService.Get("Common_No"));

    /// <summary>Titled read-only message with an action button and Close; true when the action was chosen.</summary>
    public Task<bool> DetailsAsync(string title, string message, string action) =>
        Page.DisplayAlertAsync(title, message, action, LocalizationService.Get("Common_Close"));

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

    public Task ShowErrorAsync(BusinessRuleException ex)
    {
        logger?.LogInformation("Business rule: {ErrorKey} {Args}", ex.ErrorKey, string.Join(", ", ex.Args));
        return AlertAsync(LocalizationService.Format(ex.ErrorKey, ex.Args));
    }
}

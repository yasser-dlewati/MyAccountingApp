using Microsoft.Extensions.Logging;
using MyAccountingApp.Core.Services;
using MyAccountingApp.Localization;

namespace MyAccountingApp.AppServices;

/// <summary>How the accept button of a dialog is colored.</summary>
public enum DialogTone
{
    /// <summary>Green (Primary): confirming, saving, opening settings.</summary>
    Primary,
    /// <summary>Red: deleting, revoking, replacing data.</summary>
    Danger
}

public class DialogService(ILogger<DialogService>? logger = null)
{
    private static Page Page => Shell.Current ?? Application.Current!.Windows[0].Page!;

    /// <summary>Message with an OK button; <paramref name="title"/> defaults to "Notice".</summary>
    public Task AlertAsync(string message, string? title = null) =>
        ShowAsync(title ?? LocalizationService.Get("Dialog_NoticeTitle"), message, LocalizationService.Get("Common_Ok"), null,
            DialogTone.Primary);

    /// <summary>Yes / No question; true = Yes. <see cref="DialogTone.Danger"/> colors Yes red (deleting, revoking …).</summary>
    public Task<bool> ConfirmAsync(string title, string message, DialogTone tone = DialogTone.Primary) =>
        ShowAsync(title, message, LocalizationService.Get("Common_Yes"), LocalizationService.Get("Common_No"), tone);

    /// <summary>Titled read-only message with an action button and Close; true when the action was chosen.</summary>
    public Task<bool> DetailsAsync(string title, string message, string action) =>
        ShowAsync(title, message, action, LocalizationService.Get("Common_Close"), DialogTone.Primary);

    private static Task<bool> ShowAsync(string title, string message, string accept, string? cancel, DialogTone tone)
    {
#if ANDROID
        // Rounded card with the buttons side by side instead of the stock alert dialog.
        return AppDialog.ShowAsync(title, message, accept, cancel, tone);
#else
        return cancel is null
            ? Page.DisplayAlertAsync(title, message, accept).ContinueWith(_ => true)
            : Page.DisplayAlertAsync(title, message, accept, cancel);
#endif
    }

    public Task<string?> PromptAsync(string title, string? initial = null, Keyboard? keyboard = null) =>
        Page.DisplayPromptAsync(title, null, LocalizationService.Get("Common_Ok"), LocalizationService.Get("Common_Cancel"),
            initialValue: initial ?? "", keyboard: keyboard ?? Keyboard.Default);

    /// <summary>Returns the chosen label, or null when cancelled.</summary>
    public async Task<string?> ChooseAsync(string title, params string[] options)
    {
        var cancel = LocalizationService.Get("Common_Cancel");
#if ANDROID
        // Bottom sheet instead of the centered action-sheet dialog.
        return await BottomSheetChooser.ShowAsync(title, cancel, options);
#else
        var choice = await Page.DisplayActionSheetAsync(title, cancel, null, options);
        return choice == cancel ? null : choice;
#endif
    }

    public Task ShowErrorAsync(BusinessRuleException ex)
    {
        logger?.LogInformation("Business rule: {ErrorKey} {Args}", ex.ErrorKey, string.Join(", ", ex.Args));
        return AlertAsync(LocalizationService.Format(ex.ErrorKey, ex.Args), LocalizationService.Get("Dialog_ErrorTitle"));
    }
}

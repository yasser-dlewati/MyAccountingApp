using Microsoft.Extensions.Logging;
using MyAccountingApp.Core.Services;
using MyAccountingApp.Localization;

namespace MyAccountingApp.AppServices;

/// <summary>Reads the device's address book (READ_CONTACTS) for Contacts → Import from phone.</summary>
public class PhoneContactsService(DialogService dialogs, ILogger<PhoneContactsService> logger)
{
    /// <summary>
    /// One entry per phone number (a contact with two numbers gives two rows), sorted by name; contacts without a
    /// number are left out. Null when the permission is not granted.
    /// </summary>
    public async Task<List<ContactImportInput>?> GetAllAsync()
    {
        if (!await EnsurePermissionAsync()) return null;

        var contacts = await Task.Run(async () => (await Contacts.Default.GetAllAsync()).ToList());
        var list = contacts
            .SelectMany(c => (c.Phones ?? []).Select(p => new ContactImportInput(NameOf(c), p.PhoneNumber?.Trim() ?? "")))
            .Where(c => c.Phone.Length > 0 && c.Name.Length > 0)
            .DistinctBy(c => (c.Name, ContactService.NormalizePhone(c.Phone)))
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        logger.LogInformation("Read {Count} phone numbers from {Contacts} phone contacts", list.Count, contacts.Count);
        return list;
    }

    private static string NameOf(Microsoft.Maui.ApplicationModel.Communication.Contact c) =>
        (string.IsNullOrWhiteSpace(c.DisplayName) ? $"{c.GivenName} {c.FamilyName}" : c.DisplayName).Trim();

    private async Task<bool> EnsurePermissionAsync()
    {
        if (await Permissions.CheckStatusAsync<Permissions.ContactsRead>() == PermissionStatus.Granted)
            return true;

        if (Permissions.ShouldShowRationale<Permissions.ContactsRead>())
            await dialogs.AlertAsync(LocalizationService.Get("Import_PermissionRationale"));

        var status = await Permissions.RequestAsync<Permissions.ContactsRead>();
        logger.LogInformation("Contacts permission request: {Status}", status);
        if (status == PermissionStatus.Granted)
            return true;
        if (Permissions.ShouldShowRationale<Permissions.ContactsRead>())
            return false;

        if (await dialogs.ConfirmAsync(LocalizationService.Get("Import_PermissionDenied")))
            AppInfo.Current.ShowSettingsUI();
        return false;
    }
}

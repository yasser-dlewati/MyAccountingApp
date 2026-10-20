using MyAccountingApp.Localization;
using MyAccountingApp.Views;
using ZXing.Net.Maui;

namespace MyAccountingApp.AppServices;

/// <summary>Camera barcode scanning: checks the camera permission, then shows <see cref="BarcodeScanPage"/> modally.</summary>
public class BarcodeScannerService(DialogService dialogs)
{
    /// <summary>
    /// Returns the scanned barcode, or null when the user cancelled or scanning isn't possible
    /// (no camera / permission denied — the user is told why).
    /// </summary>
    public async Task<string?> ScanAsync()
    {
        if (!BarcodeScanning.IsSupported)
        {
            await dialogs.AlertAsync(LocalizationService.Get("Scan_NoCamera"));
            return null;
        }
        if (!await EnsureCameraPermissionAsync())
            return null;

        var page = new BarcodeScanPage();
        await Shell.Current.Navigation.PushModalAsync(page);
        return await page.Result;
    }

    private async Task<bool> EnsureCameraPermissionAsync()
    {
        if (await Permissions.CheckStatusAsync<Permissions.Camera>() == PermissionStatus.Granted)
            return true;

        // Android: true only after the user has denied once, i.e. the system prompt will show a second time.
        if (Permissions.ShouldShowRationale<Permissions.Camera>())
            await dialogs.AlertAsync(LocalizationService.Get("Scan_PermissionRationale"));

        if (await Permissions.RequestAsync<Permissions.Camera>() == PermissionStatus.Granted)
            return true;

        // Denied for now but still askable: respect the choice, ask again on the next scan.
        if (Permissions.ShouldShowRationale<Permissions.Camera>())
            return false;

        // Permanently denied ("don't ask again" / denied twice): Android won't show the prompt any more,
        // so the only way back is the app's system settings page.
        if (await dialogs.ConfirmAsync(LocalizationService.Get("Scan_PermissionDenied")))
            AppInfo.Current.ShowSettingsUI();
        return false;
    }
}

using Microsoft.Extensions.Logging;
using MyAccountingApp.Localization;
using MyAccountingApp.Views;
using ZXing.Net.Maui;

namespace MyAccountingApp.AppServices;

/// <summary>Camera barcode scanning: checks the camera permission, then shows <see cref="BarcodeScanPage"/> modally.</summary>
public class BarcodeScannerService(DialogService dialogs, ILogger<BarcodeScannerService> logger)
{
    /// <summary>
    /// Returns the scanned barcode, or null when the user cancelled or scanning isn't possible
    /// (no camera / permission denied — the user is told why).
    /// </summary>
    public async Task<string?> ScanAsync()
    {
        if (!BarcodeScanning.IsSupported)
        {
            logger.LogWarning("Barcode scanning not supported on this device");
            await dialogs.AlertAsync(LocalizationService.Get("Scan_NoCamera"));
            return null;
        }
        if (!await EnsureCameraPermissionAsync())
            return null;

        var page = new BarcodeScanPage();
        await Shell.Current.Navigation.PushModalAsync(page);
        var barcode = await page.Result;
        if (barcode is null)
            logger.LogInformation("Scan cancelled");
        else
            logger.LogInformation("Scanned {Barcode}", barcode);
        return barcode;
    }

    private async Task<bool> EnsureCameraPermissionAsync()
    {
        if (await Permissions.CheckStatusAsync<Permissions.Camera>() == PermissionStatus.Granted)
            return true;

        // Android: true only after the user has denied once, i.e. the system prompt will show a second time.
        if (Permissions.ShouldShowRationale<Permissions.Camera>())
            await dialogs.AlertAsync(LocalizationService.Get("Scan_PermissionRationale"));

        var status = await Permissions.RequestAsync<Permissions.Camera>();
        logger.LogInformation("Camera permission request: {Status}", status);
        if (status == PermissionStatus.Granted)
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

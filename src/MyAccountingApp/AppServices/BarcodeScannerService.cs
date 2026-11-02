using Microsoft.Extensions.Logging;
using MyAccountingApp.Localization;
using MyAccountingApp.Views;
using ZXing.Net.Maui;

namespace MyAccountingApp.AppServices;

/// <summary>What multi-scan did with one barcode: whether an item was added (counted) and the message to show.</summary>
public record ScanOutcome(bool Added, string Message);

/// <summary>Camera barcode scanning: checks the camera permission, then shows <see cref="BarcodeScanPage"/> modally.</summary>
public class BarcodeScannerService(DialogService dialogs, ILogger<BarcodeScannerService> logger)
{
    /// <summary>
    /// Returns the scanned barcode, or null when the user cancelled or scanning isn't possible
    /// (no camera / permission denied — the user is told why).
    /// </summary>
    public async Task<string?> ScanAsync()
    {
        if (!await CanScanAsync())
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

    /// <summary>
    /// Multi-scan: every barcode goes to <paramref name="onScanned"/> and its message is shown. With the page's
    /// "Scan again" toggle off the scanner closes after the first added item; on, it keeps scanning.
    /// </summary>
    public async Task ScanManyAsync(Func<string, Task<ScanOutcome>> onScanned)
    {
        if (!await CanScanAsync())
            return;

        var count = 0;
        var page = new BarcodeScanPage(barcode =>
        {
            count++;
            logger.LogInformation("Scanned {Barcode} (multi-scan #{Count})", barcode, count);
            return onScanned(barcode);
        });
        await Shell.Current.Navigation.PushModalAsync(page);
        await page.Result;
        logger.LogInformation("Multi-scan closed after {Count} barcodes", count);
    }

    private async Task<bool> CanScanAsync()
    {
        if (!BarcodeScanning.IsSupported)
        {
            logger.LogWarning("Barcode scanning not supported on this device");
            await dialogs.AlertAsync(LocalizationService.Get("Scan_NoCamera"));
            return false;
        }
        return await EnsureCameraPermissionAsync();
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

using MyAccountingApp.Localization;
using ZXing.Net.Maui;

namespace MyAccountingApp.Views;

/// <summary>
/// Modal camera scanner, opened only through BarcodeScannerService (not a Shell route, not in DI).
/// The camera control is event-based, so this page keeps its logic in code-behind instead of a VM.
/// </summary>
public partial class BarcodeScanPage : ContentPage
{
    private readonly TaskCompletionSource<string?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? _scanned;
    private bool _closing;

    /// <summary>Completes with the barcode, or null if cancelled, once the page has closed.</summary>
    public Task<string?> Result => _result.Task;

    public BarcodeScanPage()
    {
        InitializeComponent();
        // Modal pages sit outside the Shell, so they don't inherit its FlowDirection.
        FlowDirection = LocalizationService.FlowDirection;

        // Retail / warehouse symbologies with check digits (+ QR). Codabar and RSS are left out: false positives.
        Reader.Options = new BarcodeReaderOptions
        {
            Formats = BarcodeFormat.Ean13 | BarcodeFormat.Ean8 | BarcodeFormat.UpcA | BarcodeFormat.UpcE
                      | BarcodeFormat.Code128 | BarcodeFormat.Code39 | BarcodeFormat.Code93 | BarcodeFormat.Itf
                      | BarcodeFormat.QrCode,
            AutoRotate = true,
            Multiple = false,
        };
    }

    // Raised on a camera thread.
    private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        var value = e.Results.Select(r => r.Value?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v));
        if (value is not null)
            MainThread.BeginInvokeOnMainThread(() => _ = CloseAsync(value));
    }

    private void OnTorchClicked(object? sender, EventArgs e) => Reader.IsTorchOn = !Reader.IsTorchOn;

    private void OnCancelClicked(object? sender, EventArgs e) => _ = CloseAsync(null);

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(null);
        return true;
    }

    private async Task CloseAsync(string? barcode)
    {
        if (_closing) return;
        _closing = true;
        _scanned = barcode;
        Reader.IsDetecting = false;

        if (barcode is not null)
        {
            try { HapticFeedback.Default.Perform(HapticFeedbackType.Click); }
            catch (FeatureNotSupportedException) { }
        }
        await Navigation.PopModalAsync();
        _result.TrySetResult(_scanned);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Reader.IsDetecting = false;
        Reader.IsTorchOn = false;
        // Release the camera right away (otherwise it stays open until the handler is collected).
        Reader.Handler?.DisconnectHandler();
        _result.TrySetResult(_scanned);
    }
}

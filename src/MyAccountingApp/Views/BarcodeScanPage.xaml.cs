using MyAccountingApp.AppServices;
using MyAccountingApp.Localization;
using ZXing.Net.Maui;

namespace MyAccountingApp.Views;

/// <summary>
/// Modal camera scanner, opened only through BarcodeScannerService (not a Shell route, not in DI).
/// The camera control is event-based, so this page keeps its logic in code-behind instead of a VM.
/// Single mode closes on the first barcode. Multi mode (an <c>onScanned</c> callback) hands each barcode to the
/// callback and counts the added items; with "Scan again" off it closes after the first added item, with it on it
/// keeps scanning (until it is switched off or Back is pressed).
/// </summary>
public partial class BarcodeScanPage : ContentPage
{
    private const string ContinuousPreference = "scan_continuous";
    // A barcode seen again within this time is still in view: it counts again only after leaving the frame.
    private const long InViewMs = 1000;
    // Pause after each handled barcode so the result can be read and the next item brought in.
    private const long CooldownMs = 800;

    private static readonly Color TorchOff = Color.FromArgb("#99000000");

    private readonly TaskCompletionSource<string?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<string, Task<ScanOutcome>>? _onScanned;
    private string? _scanned;
    private bool _closing;
    private bool _handling;
    private int _count;
    private string? _lastBarcode;
    private long _lastSeen;
    private long _resumeAt;

    /// <summary>Completes with the (last added) barcode, or null if nothing was scanned, once the page has closed.</summary>
    public Task<string?> Result => _result.Task;

    /// <param name="onScanned">Multi-scan mode: handles one barcode and says whether it was added + what to show.</param>
    public BarcodeScanPage(Func<string, Task<ScanOutcome>>? onScanned = null)
    {
        InitializeComponent();
        // Modal pages sit outside the Shell, so they don't inherit its FlowDirection.
        FlowDirection = LocalizationService.FlowDirection;
        _onScanned = onScanned;

        if (onScanned is not null)
        {
            CloseButton.IsVisible = false;
            CounterChip.IsVisible = ContinuousChip.IsVisible = true;
            ContinuousSwitch.IsToggled = Preferences.Default.Get(ContinuousPreference, false);
            UpdateCounter();
        }

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
        if (value is null) return;
        MainThread.BeginInvokeOnMainThread(() => _ = _onScanned is null ? CloseAsync(value) : HandleAsync(value));
    }

    private async Task HandleAsync(string barcode)
    {
        var now = Environment.TickCount64;
        var stillInView = barcode == _lastBarcode && now - _lastSeen < InViewMs;
        _lastBarcode = barcode;
        _lastSeen = now;
        if (_handling || _closing || stillInView || now < _resumeAt) return;

        _handling = true;
        Vibrate();
        var outcome = await _onScanned!(barcode);
        HintLabel.Text = outcome.Message;
        if (outcome.Added)
        {
            _scanned = barcode;
            _count++;
            UpdateCounter();
            if (!ContinuousSwitch.IsToggled)
            {
                await CloseAsync(_scanned);
                return;
            }
        }
        _resumeAt = Environment.TickCount64 + CooldownMs;
        _handling = false;
    }

    private void UpdateCounter() => CounterLabel.Text = LocalizationService.Format("Scan_Count", _count);

    // Switching "Scan again" off after scanning ends the session (back to the order).
    private void OnContinuousToggled(object? sender, ToggledEventArgs e)
    {
        Preferences.Default.Set(ContinuousPreference, e.Value);
        if (!e.Value && _count > 0)
            _ = CloseAsync(_scanned);
    }

    private void OnTorchClicked(object? sender, EventArgs e)
    {
        Reader.IsTorchOn = !Reader.IsTorchOn;
        TorchButton.BackgroundColor = Reader.IsTorchOn ? (Color)Application.Current!.Resources["Primary"] : TorchOff;
    }

    private void OnCloseClicked(object? sender, EventArgs e) => _ = CloseAsync(null);

    // Multi mode keeps what was scanned.
    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(_onScanned is null ? null : _scanned);
        return true;
    }

    private async Task CloseAsync(string? barcode)
    {
        if (_closing) return;
        _closing = true;
        _scanned = barcode;
        Reader.IsDetecting = false;

        if (barcode is not null && _onScanned is null)
            Vibrate();
        await Navigation.PopModalAsync();
        _result.TrySetResult(_scanned);
    }

    private static void Vibrate()
    {
        try { HapticFeedback.Default.Perform(HapticFeedbackType.Click); }
        catch (FeatureNotSupportedException) { }
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

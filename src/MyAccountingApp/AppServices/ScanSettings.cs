namespace MyAccountingApp.AppServices;

/// <summary>Scanner switches, saved in Preferences.</summary>
public static class ScanSettings
{
    private const string MultiScanKey = "allow_multi_scan";
    private const string ContinuousKey = "scan_continuous";
    private const string BeepModeKey = "scan_beep_mode";

    public static ScanBeepMode BeepMode
    {
        get => (ScanBeepMode)Preferences.Default.Get(BeepModeKey, (int)ScanBeepMode.EveryRead);
        set => Preferences.Default.Set(BeepModeKey, (int)value);
    }

    /// <summary>Settings → Scanner: show the "Scan again" switch on the multi-scan page. Off: one item per scan.</summary>
    public static bool MultiScanAllowed
    {
        get => Preferences.Default.Get(MultiScanKey, true);
        set => Preferences.Default.Set(MultiScanKey, value);
    }

    /// <summary>The scan page's own "Scan again" switch; only applies while <see cref="MultiScanAllowed"/>.</summary>
    public static bool Continuous
    {
        get => MultiScanAllowed && Preferences.Default.Get(ContinuousKey, false);
        set => Preferences.Default.Set(ContinuousKey, value);
    }
}

/// <summary>Settings → Scanner → Beep when scanning. Stored as int in Preferences ("scan_beep_mode").</summary>
public enum ScanBeepMode
{
    Off = 0,
    /// <summary>Every barcode read, even an unknown one.</summary>
    EveryRead = 1,
    /// <summary>Only when the barcode is a known product, even if it is out of stock.</summary>
    ProductFound = 2,
    /// <summary>Only for a known product while the stock covers the scanned quantity (stock-in orders always beep).</summary>
    InStock = 3
}

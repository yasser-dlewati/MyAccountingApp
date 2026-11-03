namespace MyAccountingApp.AppServices;

/// <summary>Scanner switches, saved in Preferences.</summary>
public static class ScanSettings
{
    private const string MultiScanKey = "allow_multi_scan";
    private const string ContinuousKey = "scan_continuous";

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

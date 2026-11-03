using Microsoft.Extensions.Logging;
#if ANDROID
using Android.Content;
using Android.Media;
#endif

namespace MyAccountingApp.AppServices;

/// <summary>
/// The "beep" when a barcode is read: <c>Resources/Raw/scan_beep.wav</c> played with a SoundPool on the media stream
/// (the volume the phone's buttons change, as barcode scanner apps do). Skipped when the phone is on silent / vibrate.
/// <see cref="Prepare"/> loads it before the camera opens (SoundPool loads asynchronously, so a sound requested
/// before it is loaded would be dropped); one pool is kept for the app's lifetime.
/// </summary>
public static class ScanBeep
{
    private const string Asset = "scan_beep.wav";
    private static ILogger? _log;

    /// <summary>Whether a read should beep, per Settings → Scanner → Beep when scanning.</summary>
    /// <param name="found">The barcode is a known product.</param>
    /// <param name="inStock">The stock on hand covers the quantity scanned so far (or the order adds stock).</param>
    public static bool ShouldBeep(bool found, bool inStock) => ScanSettings.BeepMode switch
    {
        ScanBeepMode.EveryRead => true,
        ScanBeepMode.ProductFound => found,
        ScanBeepMode.InStock => found && inStock,
        _ => false
    };

    /// <summary>The setting depends on the product, so a single scan has to look the barcode up first.</summary>
    public static bool NeedsLookup => ScanSettings.BeepMode is ScanBeepMode.ProductFound or ScanBeepMode.InStock;

#if ANDROID
    private static SoundPool? _pool;
    private static int _soundId;
    private static volatile bool _loaded;

    public static void Prepare(ILogger log)
    {
        _log = log;
        if (_pool is not null) return;
        try
        {
            var attributes = new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Media)!
                .SetContentType(AudioContentType.Sonification)!
                .Build();
            _pool = new SoundPool.Builder().SetMaxStreams(2)!.SetAudioAttributes(attributes)!.Build()!;
            _pool.LoadComplete += (_, e) =>
            {
                _loaded = e.Status == 0;
                if (!_loaded) _log?.LogWarning("Scan beep failed to load (status {Status})", e.Status);
            };
            using var fd = Platform.AppContext.Assets!.OpenFd(Asset);
            _soundId = _pool.Load(fd, 1);
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or IOException)
        {
            _log?.LogWarning(ex, "Scan beep unavailable");
            _pool = null;
        }
    }

    public static void Play()
    {
        if (_pool is null || !_loaded) return;
        var audio = Platform.AppContext.GetSystemService(Context.AudioService) as AudioManager;
        if (audio is not null && audio.RingerMode != RingerMode.Normal) return; // silent / vibrate
        if (_pool.Play(_soundId, 1f, 1f, 1, 0, 1f) == 0)
            _log?.LogWarning("Scan beep did not play");
    }
#else
    public static void Prepare(ILogger log) => _log = log;
    public static void Play() { }
#endif
}

using System.Globalization;
using System.Resources;

namespace MyAccountingApp.Localization;

/// <summary>
/// UI language (English / Arabic). Only CurrentUICulture is switched: numbers and dates are
/// always formatted with the invariant culture (see Helpers/Formats) so Arabic keeps Latin digits,
/// "." decimals and the Gregorian calendar.
/// </summary>
public static class LocalizationService
{
    public const string English = "en";
    public const string Arabic = "ar";
    private const string PreferenceKey = "language";

    private static readonly ResourceManager Resources =
        new("MyAccountingApp.Resources.Strings.AppResources", typeof(LocalizationService).Assembly);

    public static string CurrentLanguage { get; private set; } = English;

    public static bool IsRightToLeft => CurrentLanguage == Arabic;

    public static FlowDirection FlowDirection => IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>Applies the saved language, defaulting to the device language when it is Arabic.</summary>
    public static void Initialize()
    {
        var device = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == Arabic ? Arabic : English;
        Apply(Preferences.Default.Get(PreferenceKey, device));
    }

    public static void SetLanguage(string language)
    {
        Preferences.Default.Set(PreferenceKey, language);
        Apply(language);
    }

    public static string Get(string key) =>
        Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, Get(key), args);

    private static void Apply(string language)
    {
        CurrentLanguage = language == Arabic ? Arabic : English;
        var culture = new CultureInfo(CurrentLanguage);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}

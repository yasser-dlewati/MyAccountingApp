using System.Globalization;

namespace MyAccountingApp.Helpers;

/// <summary>Invariant number/date formatting and tolerant decimal parsing (accepts Arabic-Indic digits and ",", "٫").</summary>
public static class Formats
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Money(decimal value) => value.ToString("#,0.00", Inv);

    public static string Quantity(decimal value) => value.ToString("#,0.###", Inv);

    /// <summary>Plain value for an editable Entry (no thousands separators).</summary>
    public static string Input(decimal value) => value.ToString("0.###", Inv);

    public static string Date(DateTime utc) => utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", Inv);

    public static bool TryParseDecimal(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var chars = text.Trim().Select(c => c switch
        {
            >= '٠' and <= '٩' => (char)('0' + (c - '٠')), // Arabic-Indic
            >= '۰' and <= '۹' => (char)('0' + (c - '۰')), // Extended (Persian)
            ',' or '٫' => '.',
            _ => c
        }).ToArray();

        return decimal.TryParse(new string(chars), NumberStyles.Number & ~NumberStyles.AllowThousands, Inv, out value);
    }
}

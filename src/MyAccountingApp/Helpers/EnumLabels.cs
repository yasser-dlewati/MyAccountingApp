using MyAccountingApp.Core.Enums;
using MyAccountingApp.Localization;

namespace MyAccountingApp.Helpers;

public static class EnumLabels
{
    public static string Of(OrderType type) => LocalizationService.Get($"OrderType_{type}");
    public static string Of(OrderStatus status) => LocalizationService.Get($"OrderStatus_{status}");
    public static string Of(ContactType type) => LocalizationService.Get($"ContactType_{type}");

    public static List<Option<T>> Options<T>(Func<T, string> label) where T : struct, Enum =>
        Enum.GetValues<T>().Select(v => new Option<T>(label(v), v)).ToList();
}

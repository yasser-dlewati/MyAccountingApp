namespace MyAccountingApp.Core.Services;

/// <summary>
/// A rule violation to show to the user. ErrorKey is a resource key in the app's AppResources
/// (so the message can be localized); Args are format arguments for that resource string.
/// </summary>
public class BusinessRuleException(string errorKey, params object[] args)
    : Exception($"{errorKey}: {string.Join(", ", args)}")
{
    public string ErrorKey { get; } = errorKey;
    public object[] Args { get; } = args;
}

public static class ErrorKeys
{
    public const string NameRequired = "Error_NameRequired";
    public const string PhoneRequired = "Error_PhoneRequired";
    public const string BarcodeRequired = "Error_BarcodeRequired";
    public const string BarcodeExists = "Error_BarcodeExists";
    public const string PriceNegative = "Error_PriceNegative";
    public const string CategoryRequired = "Error_CategoryRequired";
    public const string NotASupplier = "Error_NotASupplier";
    public const string ContactRequired = "Error_ContactRequired";
    public const string NotACustomer = "Error_NotACustomer";
    public const string OrderHasNoItems = "Error_OrderHasNoItems";
    public const string QuantityNotPositive = "Error_QuantityNotPositive";
    public const string OrderNotDraft = "Error_OrderNotDraft";
    public const string OrderNotConfirmed = "Error_OrderNotConfirmed";
    public const string InsufficientStock = "Error_InsufficientStock";
    public const string NotFound = "Error_NotFound";
}

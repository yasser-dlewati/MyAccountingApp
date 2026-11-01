using MyAccountingApp.Core.Enums;
using MyAccountingApp.Core.Services;

namespace MyAccountingApp.AppServices;

/// <summary>
/// Settings → Walk-in switches, saved in Preferences. On: orders of that side may have no contact
/// (walk-in customer / supplier). Off: a customer / supplier must be chosen.
/// </summary>
public static class WalkInSettings
{
    private const string CustomerKey = "allow_walkin_customer";
    private const string SupplierKey = "allow_walkin_supplier";

    public static bool CustomerAllowed
    {
        get => Preferences.Default.Get(CustomerKey, true);
        set => Preferences.Default.Set(CustomerKey, value);
    }

    public static bool SupplierAllowed
    {
        get => Preferences.Default.Get(SupplierKey, true);
        set => Preferences.Default.Set(SupplierKey, value);
    }

    public static bool IsAllowed(OrderType type) =>
        OrderService.IsSalesSide(type) ? CustomerAllowed : SupplierAllowed;
}

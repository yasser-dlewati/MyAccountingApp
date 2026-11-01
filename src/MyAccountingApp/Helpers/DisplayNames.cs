using MyAccountingApp.Core.Entities;
using MyAccountingApp.Core.Enums;
using MyAccountingApp.Core.Services;
using MyAccountingApp.Localization;

namespace MyAccountingApp.Helpers;

/// <summary>Localized names for the "no row" cases: orders without a contact, products without a category.</summary>
public static class DisplayNames
{
    public static string WalkIn(OrderType type) =>
        LocalizationService.Get(OrderService.IsSalesSide(type) ? "Contact_WalkInCustomer" : "Contact_WalkInSupplier");

    public static string ContactOf(Order order) => order.Contact?.Name ?? WalkIn(order.OrderType);

    public static string CategoryOf(Product product) =>
        product.Category?.Name ?? LocalizationService.Get("Category_General");
}

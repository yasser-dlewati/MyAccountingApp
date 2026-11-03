namespace MyAccountingApp.Core.Enums;

/// <summary>Order type. Also used as ledger.document_type.</summary>
public enum OrderType
{
    Sale = 1,
    Purchase = 2,
    SaleReturn = 3,
    PurchaseReturn = 4,
    /// <summary>
    /// Ledger only (never an order): a product's opening stock, entered when it is created.
    /// <c>ledger.document_id</c> = products.id. Its unit cost counts as a purchase cost until the first purchase.
    /// </summary>
    OpeningStock = 5
}

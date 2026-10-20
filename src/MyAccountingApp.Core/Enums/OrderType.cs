namespace MyAccountingApp.Core.Enums;

/// <summary>Order type. Also used as ledger.document_type.</summary>
public enum OrderType
{
    Sale = 1,
    Purchase = 2,
    SaleReturn = 3,
    PurchaseReturn = 4
}

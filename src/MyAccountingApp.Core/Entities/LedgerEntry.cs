using MyAccountingApp.Core.Enums;

namespace MyAccountingApp.Core.Entities;

/// <summary>
/// Append-only inventory movement. Quantity is signed: + stock in (purchase, sale return),
/// - stock out (sale, purchase return). Never update or delete rows; post reversals instead.
/// </summary>
public class LedgerEntry : IHasCreatedAt
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public OrderType DocumentType { get; set; }
    /// <summary>orders.id</summary>
    public int DocumentId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal UnitPrice { get; set; }
    public DateTime CreatedAt { get; set; }

    public Product? Product { get; set; }
}

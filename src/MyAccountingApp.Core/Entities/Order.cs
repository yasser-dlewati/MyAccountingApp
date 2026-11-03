using MyAccountingApp.Core.Enums;

namespace MyAccountingApp.Core.Entities;

public class Order : IHasCreatedAt
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = "";
    public OrderType OrderType { get; set; }
    /// <summary>Null = walk-in (cash) customer / supplier.</summary>
    public int? ContactId { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Draft;
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Stamped (UTC) by AppDbContext.SaveChanges on every update.</summary>
    public DateTime? ModifiedAt { get; set; }
    /// <summary>When a confirmed order was last edited (UTC); null = never edited after confirming.</summary>
    public DateTime? EditedAt { get; set; }

    public Contact? Contact { get; set; }
    public List<OrderItem> Items { get; set; } = [];
}

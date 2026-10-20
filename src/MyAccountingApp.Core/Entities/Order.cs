using MyAccountingApp.Core.Enums;

namespace MyAccountingApp.Core.Entities;

public class Order : IHasCreatedAt
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = "";
    public OrderType OrderType { get; set; }
    public int ContactId { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Draft;
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Stamped (UTC) by AppDbContext.SaveChanges on every update.</summary>
    public DateTime? ModifiedAt { get; set; }

    public Contact? Contact { get; set; }
    public List<OrderItem> Items { get; set; } = [];
}

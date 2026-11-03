namespace MyAccountingApp.Core.Entities;

public class ProductSupplier : IHasCreatedAt
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    /// <summary>Null = the walk-in (cash) supplier, like <c>orders.contact_id</c>.</summary>
    public int? ContactId { get; set; }
    public decimal CostPrice { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public Product? Product { get; set; }
    public Contact? Contact { get; set; }
}

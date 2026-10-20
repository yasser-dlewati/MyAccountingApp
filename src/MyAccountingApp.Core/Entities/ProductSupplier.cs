namespace MyAccountingApp.Core.Entities;

public class ProductSupplier : IHasCreatedAt
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int ContactId { get; set; }
    public decimal CostPrice { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public Product? Product { get; set; }
    public Contact? Contact { get; set; }
}

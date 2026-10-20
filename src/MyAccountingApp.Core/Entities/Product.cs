namespace MyAccountingApp.Core.Entities;

public class Product : IHasCreatedAt
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int CategoryId { get; set; }
    /// <summary>Default / preferred supplier. All suppliers live in ProductSuppliers.</summary>
    public int? SupplierId { get; set; }
    public string Barcode { get; set; } = "";
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public Category? Category { get; set; }
    public Contact? Supplier { get; set; }
    public List<ProductSupplier> ProductSuppliers { get; set; } = [];
}

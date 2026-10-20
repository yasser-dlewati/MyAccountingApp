namespace MyAccountingApp.Core.Entities;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;

    public List<Product> Products { get; set; } = [];

    /// <summary>Shown by MAUI Pickers.</summary>
    public override string ToString() => Name;
}

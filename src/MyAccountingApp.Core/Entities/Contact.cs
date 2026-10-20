using MyAccountingApp.Core.Enums;

namespace MyAccountingApp.Core.Entities;

public class Contact : IHasCreatedAt
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public ContactType Type { get; set; } = ContactType.Customer;
    public DateTime CreatedAt { get; set; }

    public bool IsCustomer => Type is ContactType.Customer or ContactType.Both;
    public bool IsSupplier => Type is ContactType.Supplier or ContactType.Both;

    /// <summary>Shown by MAUI Pickers.</summary>
    public override string ToString() => Name;
}

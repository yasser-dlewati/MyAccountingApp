using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyAccountingApp.Core.Entities;

namespace MyAccountingApp.Core.Data.Configurations;

public class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> b)
    {
        b.ToTable("contacts");
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Name).HasColumnName("name").IsRequired().HasMaxLength(200);
        b.Property(x => x.Phone).HasColumnName("phone").IsRequired().HasMaxLength(30);
        b.Property(x => x.Address).HasColumnName("address").HasMaxLength(500);
        b.Property(x => x.IsActive).HasColumnName("is_active");
        b.Property(x => x.Type).HasColumnName("type");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Ignore(x => x.IsCustomer);
        b.Ignore(x => x.IsSupplier);
        b.HasIndex(x => x.Type);
    }
}

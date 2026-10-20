using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyAccountingApp.Core.Entities;

namespace MyAccountingApp.Core.Data.Configurations;

public class ProductSupplierConfiguration : IEntityTypeConfiguration<ProductSupplier>
{
    public void Configure(EntityTypeBuilder<ProductSupplier> b)
    {
        b.ToTable("product_suppliers");
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.ProductId).HasColumnName("product_id");
        b.Property(x => x.ContactId).HasColumnName("contact_id");
        b.Property(x => x.CostPrice).HasColumnName("cost_price");
        b.Property(x => x.IsActive).HasColumnName("is_active");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");

        b.HasIndex(x => new { x.ProductId, x.ContactId }).IsUnique();
        b.HasOne(x => x.Product).WithMany(p => p.ProductSuppliers).HasForeignKey(x => x.ProductId);
        b.HasOne(x => x.Contact).WithMany().HasForeignKey(x => x.ContactId);
    }
}

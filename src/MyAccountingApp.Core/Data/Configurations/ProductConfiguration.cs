using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyAccountingApp.Core.Entities;

namespace MyAccountingApp.Core.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("products");
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Name).HasColumnName("name").IsRequired().HasMaxLength(200);
        b.Property(x => x.CategoryId).HasColumnName("category_id");
        b.Property(x => x.SupplierId).HasColumnName("supplier_id");
        b.Property(x => x.Barcode).HasColumnName("barcode").IsRequired().HasMaxLength(64);
        b.Property(x => x.Price).HasColumnName("price");
        b.Property(x => x.IsActive).HasColumnName("is_active");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");

        b.HasIndex(x => x.Barcode).IsUnique();
        b.HasOne(x => x.Category).WithMany(c => c.Products).HasForeignKey(x => x.CategoryId);
        b.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId);
    }
}

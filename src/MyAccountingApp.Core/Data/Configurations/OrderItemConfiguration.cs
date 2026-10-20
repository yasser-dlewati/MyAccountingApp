using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyAccountingApp.Core.Entities;

namespace MyAccountingApp.Core.Data.Configurations;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.ToTable("order_items");
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.OrderId).HasColumnName("order_id");
        b.Property(x => x.ProductId).HasColumnName("product_id");
        b.Property(x => x.Quantity).HasColumnName("quantity");
        b.Property(x => x.UnitPrice).HasColumnName("unit_price");
        b.Ignore(x => x.LineTotal);

        // Items belong to their order (draft edits replace them); products are restricted.
        b.HasOne(x => x.Order).WithMany(o => o.Items).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
    }
}

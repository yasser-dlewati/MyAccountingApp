using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyAccountingApp.Core.Entities;

namespace MyAccountingApp.Core.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("orders");
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.OrderNumber).HasColumnName("order_number").IsRequired().HasMaxLength(20);
        b.Property(x => x.OrderType).HasColumnName("order_type");
        b.Property(x => x.ContactId).HasColumnName("contact_id");
        b.Property(x => x.Status).HasColumnName("status");
        b.Property(x => x.TotalAmount).HasColumnName("total_amount");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.ModifiedAt).HasColumnName("modified_at");

        b.HasIndex(x => x.OrderNumber).IsUnique();
        b.HasIndex(x => new { x.OrderType, x.Status });
        b.HasIndex(x => x.CreatedAt);
        b.HasOne(x => x.Contact).WithMany().HasForeignKey(x => x.ContactId);
    }
}

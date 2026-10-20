using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyAccountingApp.Core.Entities;

namespace MyAccountingApp.Core.Data.Configurations;

public class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> b)
    {
        b.ToTable("ledger");
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.ProductId).HasColumnName("product_id");
        b.Property(x => x.DocumentType).HasColumnName("document_type");
        b.Property(x => x.DocumentId).HasColumnName("document_id");
        b.Property(x => x.Quantity).HasColumnName("quantity");
        b.Property(x => x.UnitCost).HasColumnName("unit_cost");
        b.Property(x => x.UnitPrice).HasColumnName("unit_price");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");

        b.HasIndex(x => new { x.ProductId, x.CreatedAt });
        b.HasIndex(x => new { x.DocumentType, x.DocumentId });
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId);
    }
}

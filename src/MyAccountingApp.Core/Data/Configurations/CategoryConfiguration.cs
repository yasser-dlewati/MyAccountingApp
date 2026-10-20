using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyAccountingApp.Core.Entities;

namespace MyAccountingApp.Core.Data.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.ToTable("categories");
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Name).HasColumnName("name").IsRequired().HasMaxLength(100);
        b.Property(x => x.IsActive).HasColumnName("is_active");
    }
}

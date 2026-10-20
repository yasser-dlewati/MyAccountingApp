using Microsoft.EntityFrameworkCore;
using MyAccountingApp.Core.Entities;

namespace MyAccountingApp.Core.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductSupplier> ProductSuppliers => Set<ProductSupplier>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<LedgerEntry> Ledger => Set<LedgerEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Referenced rows are deactivated (is_active), never hard-deleted.
        // Only order -> order_items cascades (draft orders can be deleted with their lines).
        foreach (var fk in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
        {
            var isOrderLines = fk.DeclaringEntityType.ClrType == typeof(OrderItem)
                && fk.PrincipalEntityType.ClrType == typeof(Order);
            if (!isOrderLines)
                fk.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampTimestamps()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added && entry.Entity is IHasCreatedAt created && created.CreatedAt == default)
                created.CreatedAt = now;
            if (entry.State == EntityState.Modified && entry.Entity is Order order)
                order.ModifiedAt = now;
        }
    }
}

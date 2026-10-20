using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MyAccountingApp.Core.Data;
using MyAccountingApp.Core.Entities;
using MyAccountingApp.Core.Enums;
using MyAccountingApp.Core.Services;

namespace MyAccountingApp.Core.Tests;

/// <summary>In-memory SQLite database (migrated) shared by the services under test.</summary>
public sealed class TestDb : IDbContextFactory<AppDbContext>, IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly DbContextOptions<AppDbContext> _options;

    public TestDb()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateDbContext();
        db.Database.Migrate();

        Categories = new CategoryService(this);
        Contacts = new ContactService(this);
        Products = new ProductService(this);
        Inventory = new InventoryService(this);
        Orders = new OrderService(this);
    }

    public CategoryService Categories { get; }
    public ContactService Contacts { get; }
    public ProductService Products { get; }
    public InventoryService Inventory { get; }
    public OrderService Orders { get; }

    public AppDbContext CreateDbContext() => new(_options);

    public void Dispose() => _connection.Dispose();

    public async Task<(Contact Supplier, Contact Customer, Product Product)> SeedAsync()
    {
        var category = await Categories.SaveAsync(new Category { Name = "Drinks" });
        var supplier = await Contacts.SaveAsync(new Contact { Name = "Supplier A", Phone = "1", Type = ContactType.Supplier });
        var customer = await Contacts.SaveAsync(new Contact { Name = "Customer A", Phone = "2", Type = ContactType.Customer });
        var product = await Products.SaveAsync(new Product { Name = "Water", Barcode = "111", Price = 2m, CategoryId = category.Id });
        return (supplier, customer, product);
    }
}

using MyAccountingApp.Core.Entities;
using MyAccountingApp.Core.Enums;
using MyAccountingApp.Core.Services;

namespace MyAccountingApp.Core.Tests;

public class ProductAndInventoryTests : IDisposable
{
    private readonly TestDb _t = new();

    public void Dispose() => _t.Dispose();

    [Fact]
    public async Task Barcode_must_be_unique()
    {
        var (_, _, product) = await _t.SeedAsync();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _t.Products.SaveAsync(
            new Product { Name = "Other", Barcode = product.Barcode, Price = 1, CategoryId = product.CategoryId }));
        Assert.Equal(ErrorKeys.BarcodeExists, ex.ErrorKey);
    }

    [Fact]
    public async Task Default_supplier_must_be_a_supplier()
    {
        var (_, customer, product) = await _t.SeedAsync();
        product.SupplierId = customer.Id;
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _t.Products.SaveAsync(product));
        Assert.Equal(ErrorKeys.NotASupplier, ex.ErrorKey);
    }

    [Fact]
    public async Task Search_finds_by_name_or_barcode_and_hides_inactive()
    {
        var (_, _, product) = await _t.SeedAsync();
        Assert.Single(await _t.Products.SearchAsync("Wat"));
        Assert.Single(await _t.Products.SearchAsync("111"));

        await _t.Products.SetActiveAsync(product.Id, false);
        Assert.Empty(await _t.Products.SearchAsync("Wat"));
        Assert.Single(await _t.Products.SearchAsync("Wat", includeInactive: true));
    }

    [Fact]
    public async Task Contact_search_by_role_includes_both()
    {
        await _t.SeedAsync();
        await _t.Contacts.SaveAsync(new Contact { Name = "Both Co", Phone = "3", Type = ContactType.Both });

        Assert.Equal(2, (await _t.Contacts.SearchAsync(role: ContactType.Supplier)).Count);
        Assert.Equal(2, (await _t.Contacts.SearchAsync(role: ContactType.Customer)).Count);
        Assert.Equal(3, (await _t.Contacts.SearchAsync()).Count);
    }

    [Fact]
    public async Task Stock_report_lists_quantity_cost_and_low_stock()
    {
        var (supplier, _, product) = await _t.SeedAsync();
        var other = await _t.Products.SaveAsync(new Product { Name = "Juice", Barcode = "222", Price = 3, CategoryId = product.CategoryId });
        var order = await _t.Orders.CreateDraftAsync(OrderType.Purchase, supplier.Id,
            [new OrderLineInput(product.Id, 10, 1.2m), new OrderLineInput(other.Id, 2, 2m)]);
        await _t.Orders.ConfirmAsync(order.Id);

        var report = await _t.Inventory.GetStockReportAsync();
        var water = report.Single(r => r.ProductId == product.Id);
        Assert.Equal(10, water.Quantity);
        Assert.Equal(1.2m, water.LastCost);
        Assert.Equal(12m, water.CostValue);

        var low = await _t.Inventory.GetStockReportAsync(maxQuantity: 5);
        Assert.Equal(other.Id, Assert.Single(low).ProductId);
    }
}

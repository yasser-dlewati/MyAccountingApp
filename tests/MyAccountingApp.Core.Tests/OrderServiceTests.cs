using Microsoft.EntityFrameworkCore;
using MyAccountingApp.Core.Enums;
using MyAccountingApp.Core.Services;

namespace MyAccountingApp.Core.Tests;

public class OrderServiceTests : IDisposable
{
    private readonly TestDb _t = new();

    public void Dispose() => _t.Dispose();

    private async Task<int> ConfirmedAsync(OrderType type, int contactId, int productId, decimal qty, decimal price)
    {
        var order = await _t.Orders.CreateDraftAsync(type, contactId, [new OrderLineInput(productId, qty, price)]);
        await _t.Orders.ConfirmAsync(order.Id);
        return order.Id;
    }

    [Fact]
    public async Task Confirming_purchase_and_sale_posts_signed_ledger_rows()
    {
        var (supplier, customer, product) = await _t.SeedAsync();

        await ConfirmedAsync(OrderType.Purchase, supplier.Id, product.Id, 10, 1.25m);
        Assert.Equal(10, await _t.Inventory.GetStockAsync(product.Id));

        var saleId = await ConfirmedAsync(OrderType.Sale, customer.Id, product.Id, 3, 2m);
        Assert.Equal(7, await _t.Inventory.GetStockAsync(product.Id));

        await using var db = _t.CreateDbContext();
        var saleRow = await db.Ledger.SingleAsync(l => l.DocumentId == saleId && l.DocumentType == OrderType.Sale);
        Assert.Equal(-3, saleRow.Quantity);
        Assert.Equal(1.25m, saleRow.UnitCost);   // last purchase cost
        Assert.Equal(2m, saleRow.UnitPrice);

        var order = await _t.Orders.GetAsync(saleId);
        Assert.Equal(OrderStatus.Confirmed, order!.Status);
        Assert.Equal(6m, order.TotalAmount);
        Assert.StartsWith("S-", order.OrderNumber);
    }

    [Fact]
    public async Task Returns_move_stock_in_the_right_direction()
    {
        var (supplier, customer, product) = await _t.SeedAsync();
        await ConfirmedAsync(OrderType.Purchase, supplier.Id, product.Id, 10, 1m);
        await ConfirmedAsync(OrderType.Sale, customer.Id, product.Id, 4, 2m);
        await ConfirmedAsync(OrderType.SaleReturn, customer.Id, product.Id, 1, 2m);
        await ConfirmedAsync(OrderType.PurchaseReturn, supplier.Id, product.Id, 2, 1m);

        Assert.Equal(10 - 4 + 1 - 2, await _t.Inventory.GetStockAsync(product.Id));
    }

    [Fact]
    public async Task Cancel_posts_reversal_and_keeps_original_rows()
    {
        var (supplier, customer, product) = await _t.SeedAsync();
        await ConfirmedAsync(OrderType.Purchase, supplier.Id, product.Id, 10, 1m);
        var saleId = await ConfirmedAsync(OrderType.Sale, customer.Id, product.Id, 3, 2m);

        await _t.Orders.CancelAsync(saleId);

        Assert.Equal(10, await _t.Inventory.GetStockAsync(product.Id));
        await using var db = _t.CreateDbContext();
        var rows = await db.Ledger.Where(l => l.DocumentId == saleId && l.DocumentType == OrderType.Sale).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(OrderStatus.Cancelled, (await _t.Orders.GetAsync(saleId))!.Status);
    }

    [Fact]
    public async Task Last_purchase_cost_ignores_cancelled_purchases()
    {
        var (supplier, _, product) = await _t.SeedAsync();
        await ConfirmedAsync(OrderType.Purchase, supplier.Id, product.Id, 5, 1m);
        var second = await ConfirmedAsync(OrderType.Purchase, supplier.Id, product.Id, 5, 1.5m);
        Assert.Equal(1.5m, await _t.Inventory.GetLastPurchaseCostAsync(product.Id));

        await _t.Orders.CancelAsync(second);
        Assert.Equal(1m, await _t.Inventory.GetLastPurchaseCostAsync(product.Id));
    }

    [Fact]
    public async Task Confirming_purchase_updates_supplier_cost_and_default_supplier()
    {
        var (supplier, _, product) = await _t.SeedAsync();
        await ConfirmedAsync(OrderType.Purchase, supplier.Id, product.Id, 5, 1.75m);

        var loaded = await _t.Products.GetAsync(product.Id);
        Assert.Equal(supplier.Id, loaded!.SupplierId);
        Assert.Equal(1.75m, Assert.Single(loaded.ProductSuppliers).CostPrice);
    }

    [Fact]
    public async Task Sale_beyond_stock_is_rejected_unless_allowed()
    {
        var (_, customer, product) = await _t.SeedAsync();
        var order = await _t.Orders.CreateDraftAsync(OrderType.Sale, customer.Id, [new OrderLineInput(product.Id, 1, 2m)]);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _t.Orders.ConfirmAsync(order.Id));
        Assert.Equal(ErrorKeys.InsufficientStock, ex.ErrorKey);

        await _t.Orders.ConfirmAsync(order.Id, allowNegativeStock: true);
        Assert.Equal(-1, await _t.Inventory.GetStockAsync(product.Id));
    }

    [Fact]
    public async Task Confirmed_orders_cannot_be_edited_or_deleted()
    {
        var (supplier, _, product) = await _t.SeedAsync();
        var id = await ConfirmedAsync(OrderType.Purchase, supplier.Id, product.Id, 1, 1m);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _t.Orders.UpdateDraftAsync(id, supplier.Id, [new OrderLineInput(product.Id, 2, 1m)]));
        await Assert.ThrowsAsync<BusinessRuleException>(() => _t.Orders.DeleteDraftAsync(id));
        await Assert.ThrowsAsync<BusinessRuleException>(() => _t.Orders.ConfirmAsync(id));
    }

    [Fact]
    public async Task Draft_can_be_edited_and_order_numbers_increment_per_type()
    {
        var (supplier, customer, product) = await _t.SeedAsync();
        var first = await _t.Orders.CreateDraftAsync(OrderType.Sale, customer.Id, [new OrderLineInput(product.Id, 1, 2m)]);
        var second = await _t.Orders.CreateDraftAsync(OrderType.Sale, customer.Id, [new OrderLineInput(product.Id, 1, 2m)]);
        var purchase = await _t.Orders.CreateDraftAsync(OrderType.Purchase, supplier.Id, [new OrderLineInput(product.Id, 1, 1m)]);

        Assert.Equal("S-000001", first.OrderNumber);
        Assert.Equal("S-000002", second.OrderNumber);
        Assert.Equal("P-000001", purchase.OrderNumber);

        await _t.Orders.UpdateDraftAsync(first.Id, customer.Id,
            [new OrderLineInput(product.Id, 2, 2m), new OrderLineInput(product.Id, 1, 3m)]);
        var reloaded = await _t.Orders.GetAsync(first.Id);
        Assert.Equal(2, reloaded!.Items.Count);
        Assert.Equal(7m, reloaded.TotalAmount);
        Assert.NotNull(reloaded.ModifiedAt);
    }

    [Fact]
    public async Task Contact_role_must_match_order_type()
    {
        var (supplier, customer, product) = await _t.SeedAsync();
        var lines = new[] { new OrderLineInput(product.Id, 1, 1m) };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _t.Orders.CreateDraftAsync(OrderType.Sale, supplier.Id, lines));
        Assert.Equal(ErrorKeys.NotACustomer, ex.ErrorKey);
        ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _t.Orders.CreateDraftAsync(OrderType.Purchase, customer.Id, lines));
        Assert.Equal(ErrorKeys.NotASupplier, ex.ErrorKey);
    }

    [Fact]
    public async Task Sales_summary_nets_returns_and_cancellations()
    {
        var (supplier, customer, product) = await _t.SeedAsync();
        await ConfirmedAsync(OrderType.Purchase, supplier.Id, product.Id, 10, 1m);
        await ConfirmedAsync(OrderType.Sale, customer.Id, product.Id, 4, 2.5m);
        var cancelled = await ConfirmedAsync(OrderType.Sale, customer.Id, product.Id, 2, 2.5m);
        await _t.Orders.CancelAsync(cancelled);
        await ConfirmedAsync(OrderType.SaleReturn, customer.Id, product.Id, 1, 2.5m);

        var summary = await _t.Inventory.GetSalesSummaryAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));
        Assert.Equal(7.5m, summary.Revenue);
        Assert.Equal(3m, summary.Cost);
        Assert.Equal(4.5m, summary.Profit);
        Assert.Equal(1, summary.OrderCount);
    }
}

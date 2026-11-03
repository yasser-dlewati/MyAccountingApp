using Microsoft.EntityFrameworkCore;
using MyAccountingApp.Core.Entities;
using MyAccountingApp.Core.Enums;
using MyAccountingApp.Core.Services;

namespace MyAccountingApp.Core.Tests;

public class ConfirmedEditAndOpeningStockTests : IDisposable
{
    private readonly TestDb _t = new();

    public void Dispose() => _t.Dispose();

    private async Task<int> ConfirmedAsync(OrderType type, int? contactId, params OrderLineInput[] lines)
    {
        var order = await _t.Orders.CreateDraftAsync(type, contactId, lines);
        await _t.Orders.ConfirmAsync(order.Id, allowNegativeStock: true);
        return order.Id;
    }

    [Fact]
    public async Task Editing_a_confirmed_sale_reposts_the_stock_and_flags_it()
    {
        var (supplier, customer, product) = await _t.SeedAsync();
        var other = await _t.Products.SaveAsync(new Product { Name = "Juice", Barcode = "222", Price = 5m }, categoryRequired: false);
        await ConfirmedAsync(OrderType.Purchase, supplier.Id, new(product.Id, 10, 1m), new(other.Id, 10, 3m));
        var saleId = await ConfirmedAsync(OrderType.Sale, customer.Id, new OrderLineInput(product.Id, 4, 2m));
        var before = await _t.Orders.GetAsync(saleId);
        Assert.Null(before!.EditedAt);

        // Cost changes after the sale: the product already on the order keeps its posted cost (1).
        await ConfirmedAsync(OrderType.Purchase, supplier.Id, new OrderLineInput(product.Id, 1, 1.5m));
        await _t.Orders.UpdateConfirmedAsync(saleId, customer.Id, [new(product.Id, 2, 2.5m), new(other.Id, 1, 5m)]);

        Assert.Equal(9, await _t.Inventory.GetStockAsync(product.Id)); // 10 + 1 - 2
        Assert.Equal(9, await _t.Inventory.GetStockAsync(other.Id));
        var order = await _t.Orders.GetAsync(saleId);
        Assert.Equal(OrderStatus.Confirmed, order!.Status);
        Assert.NotNull(order.EditedAt);
        Assert.Equal(10m, order.TotalAmount); // 2 × 2.5 + 5
        Assert.Equal(2, order.Items.Count);

        await using var db = _t.CreateDbContext();
        var rows = await db.Ledger.Where(l => l.DocumentType == OrderType.Sale && l.DocumentId == saleId).ToListAsync();
        Assert.Equal(10m, rows.Sum(l => -l.Quantity * l.UnitPrice)); // net revenue = the new lines
        Assert.Equal(2m, rows.Where(l => l.ProductId == product.Id).Sum(l => -l.Quantity * l.UnitCost));
        Assert.Equal(3m, rows.Where(l => l.ProductId == other.Id).Sum(l => -l.Quantity * l.UnitCost));
        // Correction rows keep the original posting time.
        Assert.Single(rows.Select(l => l.CreatedAt).Distinct());

        // Edited twice, then cancelled: everything nets to zero.
        await _t.Orders.UpdateConfirmedAsync(saleId, null, [new(product.Id, 1, 2m)]);
        Assert.Equal(10, await _t.Inventory.GetStockAsync(product.Id));
        Assert.Equal(10, await _t.Inventory.GetStockAsync(other.Id));
        await _t.Orders.CancelAsync(saleId);
        Assert.Equal(11, await _t.Inventory.GetStockAsync(product.Id));
        var summary = await _t.Inventory.GetSalesSummaryAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));
        Assert.Equal(0m, summary.Revenue);
    }

    [Fact]
    public async Task Editing_a_confirmed_sale_checks_stock_with_its_own_quantity_given_back()
    {
        var (supplier, customer, product) = await _t.SeedAsync();
        await ConfirmedAsync(OrderType.Purchase, supplier.Id, new OrderLineInput(product.Id, 5, 1m));
        var saleId = await ConfirmedAsync(OrderType.Sale, customer.Id, new OrderLineInput(product.Id, 3, 2m));

        await _t.Orders.UpdateConfirmedAsync(saleId, customer.Id, [new(product.Id, 5, 2m)]); // 5 available again
        Assert.Equal(0, await _t.Inventory.GetStockAsync(product.Id));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _t.Orders.UpdateConfirmedAsync(saleId, customer.Id, [new(product.Id, 6, 2m)]));
        Assert.Equal(ErrorKeys.InsufficientStock, ex.ErrorKey);
        Assert.Equal(0, await _t.Inventory.GetStockAsync(product.Id)); // rolled back
        Assert.Equal(5, (await _t.Orders.GetAsync(saleId))!.Items.Single().Quantity);
    }

    [Fact]
    public async Task Only_confirmed_orders_can_be_edited_and_need_items()
    {
        var (_, customer, product) = await _t.SeedAsync();
        var draft = await _t.Orders.CreateDraftAsync(OrderType.Sale, customer.Id, [new(product.Id, 1, 2m)]);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _t.Orders.UpdateConfirmedAsync(draft.Id, customer.Id, [new(product.Id, 1, 2m)]));
        Assert.Equal(ErrorKeys.OrderNotConfirmed, ex.ErrorKey);

        await _t.Orders.ConfirmAsync(draft.Id, allowNegativeStock: true);
        ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _t.Orders.UpdateConfirmedAsync(draft.Id, customer.Id, [], allowNegativeStock: true));
        Assert.Equal(ErrorKeys.OrderHasNoItems, ex.ErrorKey);
        ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _t.Orders.UpdateConfirmedAsync(draft.Id, null, [new(product.Id, 1, 2m)], true, allowWalkIn: false));
        Assert.Equal(ErrorKeys.ContactRequired, ex.ErrorKey);
    }

    [Fact]
    public async Task New_product_opening_stock_is_posted_and_costed()
    {
        var (supplier, customer, _) = await _t.SeedAsync();
        var product = await _t.Products.SaveAsync(new Product { Name = "Tea", Barcode = "333", Price = 4m },
            categoryRequired: false, openingQuantity: 12, openingCost: 2.5m);

        Assert.Equal(12, await _t.Inventory.GetStockAsync(product.Id));
        Assert.Equal(2.5m, await _t.Inventory.GetLastPurchaseCostAsync(product.Id));
        var stock = (await _t.Inventory.GetStockReportAsync()).Single(r => r.ProductId == product.Id);
        Assert.Equal(30m, stock.CostValue);

        // Saving it again does not post more opening stock.
        await _t.Products.SaveAsync(product, categoryRequired: false, openingQuantity: 5);
        Assert.Equal(12, await _t.Inventory.GetStockAsync(product.Id));

        // A later purchase replaces the opening cost; opening stock is not a purchase in reports.
        await ConfirmedAsync(OrderType.Purchase, supplier.Id, new OrderLineInput(product.Id, 1, 3m));
        Assert.Equal(3m, await _t.Inventory.GetLastPurchaseCostAsync(product.Id));
        var dashboard = await _t.Reports.GetDashboardAsync(ReportPeriod.Day);
        Assert.Equal(3m, dashboard.Purchases);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _t.Products.SaveAsync(
            new Product { Name = "Bad", Barcode = "444" }, categoryRequired: false, openingQuantity: -1));
        Assert.Equal(ErrorKeys.OpeningQuantityNegative, ex.ErrorKey);
    }
}

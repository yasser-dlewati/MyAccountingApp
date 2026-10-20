using Microsoft.EntityFrameworkCore;
using MyAccountingApp.Core.Data;
using MyAccountingApp.Core.Enums;

namespace MyAccountingApp.Core.Services;

public record StockRow(int ProductId, string ProductName, string Barcode, decimal Quantity, decimal LastCost, decimal Price)
{
    public decimal CostValue => Quantity * LastCost;
}

public record SalesSummary(decimal Revenue, decimal Cost, int OrderCount)
{
    public decimal Profit => Revenue - Cost;
}

public class InventoryService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<decimal> GetStockAsync(int productId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await GetStockAsync(db, productId);
    }

    public async Task<decimal> GetLastPurchaseCostAsync(int productId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await GetLastPurchaseCostAsync(db, productId);
    }

    /// <summary>Stock on hand for every active product (or below/at <paramref name="maxQuantity"/> when given).</summary>
    public async Task<List<StockRow>> GetStockReportAsync(decimal? maxQuantity = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var products = await db.Products.AsNoTracking().Where(p => p.IsActive)
            .Select(p => new { p.Id, p.Name, p.Barcode, p.Price })
            .ToListAsync();

        var quantities = await db.Ledger
            .GroupBy(l => l.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(l => l.Quantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Quantity);

        var lastCosts = await LastPurchaseCosts(db)
            .ToDictionaryAsync(x => x.ProductId, x => x.UnitCost);

        return products
            .Select(p => new StockRow(p.Id, p.Name, p.Barcode,
                quantities.GetValueOrDefault(p.Id), lastCosts.GetValueOrDefault(p.Id), p.Price))
            .Where(r => maxQuantity is null || r.Quantity <= maxQuantity)
            .OrderBy(r => r.ProductName)
            .ToList();
    }

    /// <summary>Revenue / cost / profit of sales minus sale returns between two UTC instants.</summary>
    public async Task<SalesSummary> GetSalesSummaryAsync(DateTime fromUtc, DateTime toUtc)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        // Sales are stored with negative quantity, returns and cancellation reversals with positive,
        // so -quantity nets them out. Aggregated client-side: decimals are TEXT in SQLite.
        var rows = await db.Ledger.AsNoTracking()
            .Where(l => (l.DocumentType == OrderType.Sale || l.DocumentType == OrderType.SaleReturn)
                && l.CreatedAt >= fromUtc && l.CreatedAt < toUtc)
            .Select(l => new { l.DocumentId, l.DocumentType, l.Quantity, l.UnitCost, l.UnitPrice })
            .ToListAsync();

        var orderCount = await db.Orders.CountAsync(o => o.OrderType == OrderType.Sale
            && o.Status == OrderStatus.Confirmed && o.CreatedAt >= fromUtc && o.CreatedAt < toUtc);

        return new SalesSummary(
            rows.Sum(r => -r.Quantity * r.UnitPrice),
            rows.Sum(r => -r.Quantity * r.UnitCost),
            orderCount);
    }

    internal static Task<decimal> GetStockAsync(AppDbContext db, int productId) =>
        db.Ledger.Where(l => l.ProductId == productId).SumAsync(l => l.Quantity);

    internal static async Task<decimal> GetLastPurchaseCostAsync(AppDbContext db, int productId) =>
        await LastPurchaseCosts(db).Where(x => x.ProductId == productId).Select(x => x.UnitCost).FirstOrDefaultAsync();

    /// <summary>
    /// Unit cost of the latest stock-in line of each product's confirmed purchases
    /// (cancelled purchases are skipped).
    /// </summary>
    private static IQueryable<ProductCost> LastPurchaseCosts(AppDbContext db)
    {
        var purchaseLines = db.Ledger.Where(l => l.DocumentType == OrderType.Purchase
            && db.Orders.Any(o => o.Id == l.DocumentId && o.Status == OrderStatus.Confirmed));

        return purchaseLines
            .Where(l => l.Id == purchaseLines.Where(x => x.ProductId == l.ProductId).Max(x => x.Id))
            .Select(l => new ProductCost { ProductId = l.ProductId, UnitCost = l.UnitCost });
    }

    private class ProductCost
    {
        public int ProductId { get; init; }
        public decimal UnitCost { get; init; }
    }
}

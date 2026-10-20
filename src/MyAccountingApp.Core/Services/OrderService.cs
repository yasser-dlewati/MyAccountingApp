using Microsoft.EntityFrameworkCore;
using MyAccountingApp.Core.Data;
using MyAccountingApp.Core.Entities;
using MyAccountingApp.Core.Enums;

namespace MyAccountingApp.Core.Services;

public record OrderLineInput(int ProductId, decimal Quantity, decimal UnitPrice);

public record ShortageInfo(int ProductId, string ProductName, decimal Available, decimal Requested);

public class OrderService(IDbContextFactory<AppDbContext> dbFactory)
{
    public static string PrefixFor(OrderType type) => type switch
    {
        OrderType.Sale => "S",
        OrderType.Purchase => "P",
        OrderType.SaleReturn => "SR",
        OrderType.PurchaseReturn => "PR",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    /// <summary>+1 for stock in, -1 for stock out.</summary>
    public static int StockDirection(OrderType type) =>
        type is OrderType.Purchase or OrderType.SaleReturn ? 1 : -1;

    public static bool IsSalesSide(OrderType type) => type is OrderType.Sale or OrderType.SaleReturn;

    public async Task<List<Order>> GetOrdersAsync(OrderType? type = null, OrderStatus? status = null,
        DateTime? fromUtc = null, DateTime? toUtc = null, string? search = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.Orders.AsNoTracking().Include(o => o.Contact).AsQueryable();
        if (type is not null) query = query.Where(o => o.OrderType == type);
        if (status is not null) query = query.Where(o => o.Status == status);
        if (fromUtc is not null) query = query.Where(o => o.CreatedAt >= fromUtc);
        if (toUtc is not null) query = query.Where(o => o.CreatedAt < toUtc);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var t = search.Trim();
            query = query.Where(o => o.OrderNumber.Contains(t) || o.Contact!.Name.Contains(t));
        }
        return await query.OrderByDescending(o => o.Id).ToListAsync();
    }

    public async Task<Order?> GetAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Orders.AsNoTracking()
            .Include(o => o.Contact)
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<Order> CreateDraftAsync(OrderType type, int contactId, IReadOnlyList<OrderLineInput> lines)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await ValidateContactAsync(db, type, contactId);
        ValidateLines(lines);

        var order = new Order
        {
            OrderType = type,
            ContactId = contactId,
            Status = OrderStatus.Draft,
            OrderNumber = await NextOrderNumberAsync(db, type),
            Items = lines.Select(ToItem).ToList()
        };
        order.TotalAmount = order.Items.Sum(i => i.LineTotal);

        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    public async Task<Order> UpdateDraftAsync(int orderId, int contactId, IReadOnlyList<OrderLineInput> lines)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var order = await LoadAsync(db, orderId);
        if (order.Status != OrderStatus.Draft)
            throw new BusinessRuleException(ErrorKeys.OrderNotDraft, order.OrderNumber);

        await ValidateContactAsync(db, order.OrderType, contactId);
        ValidateLines(lines);

        order.ContactId = contactId;
        db.OrderItems.RemoveRange(order.Items);
        order.Items = lines.Select(ToItem).ToList();
        order.TotalAmount = order.Items.Sum(i => i.LineTotal);

        await db.SaveChangesAsync();
        return order;
    }

    public async Task DeleteDraftAsync(int orderId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var order = await LoadAsync(db, orderId);
        if (order.Status != OrderStatus.Draft)
            throw new BusinessRuleException(ErrorKeys.OrderNotDraft, order.OrderNumber);
        db.Orders.Remove(order);
        await db.SaveChangesAsync();
    }

    /// <summary>Stock-out lines that exceed stock on hand. Empty for stock-in orders.</summary>
    public async Task<List<ShortageInfo>> GetShortagesAsync(int orderId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var order = await LoadAsync(db, orderId);
        return await GetShortagesAsync(db, order);
    }

    /// <summary>
    /// Confirms a draft: posts signed ledger rows, recalculates the total and, for purchases,
    /// refreshes the supplier cost price — all in one transaction.
    /// Throws <see cref="ErrorKeys.InsufficientStock"/> unless <paramref name="allowNegativeStock"/>.
    /// </summary>
    public async Task<Order> ConfirmAsync(int orderId, bool allowNegativeStock = false)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var order = await LoadAsync(db, orderId);
        if (order.Status != OrderStatus.Draft)
            throw new BusinessRuleException(ErrorKeys.OrderNotDraft, order.OrderNumber);
        if (order.Items.Count == 0)
            throw new BusinessRuleException(ErrorKeys.OrderHasNoItems);

        if (!allowNegativeStock)
        {
            var shortages = await GetShortagesAsync(db, order);
            if (shortages.Count > 0)
                throw new BusinessRuleException(ErrorKeys.InsufficientStock,
                    string.Join(", ", shortages.Select(s => s.ProductName)));
        }

        var direction = StockDirection(order.OrderType);
        foreach (var item in order.Items)
        {
            var unitCost = order.OrderType == OrderType.Purchase
                ? item.UnitPrice
                : await InventoryService.GetLastPurchaseCostAsync(db, item.ProductId);

            db.Ledger.Add(new LedgerEntry
            {
                ProductId = item.ProductId,
                DocumentType = order.OrderType,
                DocumentId = order.Id,
                Quantity = direction * item.Quantity,
                UnitCost = unitCost,
                UnitPrice = item.UnitPrice
            });
        }

        if (order.OrderType == OrderType.Purchase)
            await UpdateSupplierCostsAsync(db, order);

        order.TotalAmount = order.Items.Sum(i => i.LineTotal);
        order.Status = OrderStatus.Confirmed;

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return order;
    }

    /// <summary>Cancels a confirmed order by posting reversing ledger rows (the original rows stay).</summary>
    public async Task<Order> CancelAsync(int orderId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var order = await LoadAsync(db, orderId);
        if (order.Status != OrderStatus.Confirmed)
            throw new BusinessRuleException(ErrorKeys.OrderNotConfirmed, order.OrderNumber);

        var posted = await db.Ledger.AsNoTracking()
            .Where(l => l.DocumentType == order.OrderType && l.DocumentId == order.Id)
            .ToListAsync();

        foreach (var row in posted)
        {
            db.Ledger.Add(new LedgerEntry
            {
                ProductId = row.ProductId,
                DocumentType = row.DocumentType,
                DocumentId = row.DocumentId,
                Quantity = -row.Quantity,
                UnitCost = row.UnitCost,
                UnitPrice = row.UnitPrice
            });
        }

        order.Status = OrderStatus.Cancelled;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return order;
    }

    private static async Task<Order> LoadAsync(AppDbContext db, int orderId) =>
        await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw new BusinessRuleException(ErrorKeys.NotFound);

    private static async Task<List<ShortageInfo>> GetShortagesAsync(AppDbContext db, Order order)
    {
        if (StockDirection(order.OrderType) > 0)
            return [];

        var result = new List<ShortageInfo>();
        foreach (var group in order.Items.GroupBy(i => i.ProductId))
        {
            var requested = group.Sum(i => i.Quantity);
            var available = await InventoryService.GetStockAsync(db, group.Key);
            if (requested > available)
            {
                var name = await db.Products.Where(p => p.Id == group.Key).Select(p => p.Name).FirstAsync();
                result.Add(new ShortageInfo(group.Key, name, available, requested));
            }
        }
        return result;
    }

    private static async Task UpdateSupplierCostsAsync(AppDbContext db, Order order)
    {
        foreach (var item in order.Items)
        {
            await ProductService.UpsertSupplierCostAsync(db, item.ProductId, order.ContactId, item.UnitPrice);
            await db.SaveChangesAsync(); // so a repeated product in the same order finds the new link
        }

        // First supplier a product is bought from becomes its default supplier.
        var productIds = order.Items.Select(i => i.ProductId).Distinct().ToList();
        await db.Products.Where(p => productIds.Contains(p.Id) && p.SupplierId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.SupplierId, order.ContactId));
    }

    private static async Task ValidateContactAsync(AppDbContext db, OrderType type, int contactId)
    {
        var contact = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contactId)
            ?? throw new BusinessRuleException(ErrorKeys.NotFound);
        if (IsSalesSide(type) && !contact.IsCustomer)
            throw new BusinessRuleException(ErrorKeys.NotACustomer, contact.Name);
        if (!IsSalesSide(type) && !contact.IsSupplier)
            throw new BusinessRuleException(ErrorKeys.NotASupplier, contact.Name);
    }

    private static void ValidateLines(IReadOnlyList<OrderLineInput> lines)
    {
        if (lines.Count == 0)
            throw new BusinessRuleException(ErrorKeys.OrderHasNoItems);
        if (lines.Any(l => l.Quantity <= 0))
            throw new BusinessRuleException(ErrorKeys.QuantityNotPositive);
        if (lines.Any(l => l.UnitPrice < 0))
            throw new BusinessRuleException(ErrorKeys.PriceNegative);
    }

    private static OrderItem ToItem(OrderLineInput line) =>
        new() { ProductId = line.ProductId, Quantity = line.Quantity, UnitPrice = line.UnitPrice };

    private static async Task<string> NextOrderNumberAsync(AppDbContext db, OrderType type)
    {
        var prefix = PrefixFor(type) + "-";
        var last = await db.Orders.Where(o => o.OrderType == type)
            .OrderByDescending(o => o.Id).Select(o => o.OrderNumber).FirstOrDefaultAsync();
        var next = last is not null && int.TryParse(last.AsSpan(prefix.Length), out var n) ? n + 1 : 1;
        return $"{prefix}{next:D6}";
    }
}

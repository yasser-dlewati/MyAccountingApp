using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MyAccountingApp.Core.Data;
using MyAccountingApp.Core.Entities;
using MyAccountingApp.Core.Enums;

namespace MyAccountingApp.Core.Services;

public record OrderLineInput(int ProductId, decimal Quantity, decimal UnitPrice);

public record ShortageInfo(int ProductId, string ProductName, decimal Available, decimal Requested);

public record BelowCostInfo(int ProductId, string ProductName, decimal Cost, decimal SellingPrice);

public class OrderService(IDbContextFactory<AppDbContext> dbFactory, ILogger<OrderService>? logger = null)
{
    private readonly ILogger _log = logger ?? NullLogger<OrderService>.Instance;

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

    /// <param name="salesSide">true = sales + sale returns, false = purchases + purchase returns, null = all.</param>
    public async Task<List<Order>> GetOrdersAsync(OrderType? type = null, OrderStatus? status = null,
        DateTime? fromUtc = null, DateTime? toUtc = null, string? search = null, bool? salesSide = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.Orders.AsNoTracking().Include(o => o.Contact).AsQueryable();
        if (type is not null) query = query.Where(o => o.OrderType == type);
        if (salesSide is not null)
            query = query.Where(o => (o.OrderType == OrderType.Sale || o.OrderType == OrderType.SaleReturn) == salesSide);
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

    /// <summary>
    /// Drafts may be incomplete (no contact, no items, zero quantities); <see cref="ConfirmAsync"/> checks those.
    /// A contact that is set must still match the order side.
    /// </summary>
    public async Task<Order> CreateDraftAsync(OrderType type, int? contactId, IReadOnlyList<OrderLineInput> lines)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await ValidateContactAsync(db, type, contactId);
        ValidateDraftLines(lines);

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
        _log.LogInformation("Draft {OrderNumber} created (id {OrderId}, {Lines} lines, contact {ContactId})",
            order.OrderNumber, order.Id, order.Items.Count, contactId);
        return order;
    }

    public async Task<Order> UpdateDraftAsync(int orderId, int? contactId, IReadOnlyList<OrderLineInput> lines)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var order = await LoadAsync(db, orderId);
        if (order.Status != OrderStatus.Draft)
            throw new BusinessRuleException(ErrorKeys.OrderNotDraft, order.OrderNumber);

        await ValidateContactAsync(db, order.OrderType, contactId);
        ValidateDraftLines(lines);

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
        _log.LogInformation("Draft {OrderNumber} deleted (id {OrderId})", order.OrderNumber, order.Id);
    }

    /// <summary>Stock-out lines that exceed stock on hand. Empty for stock-in orders.</summary>
    public async Task<List<ShortageInfo>> GetShortagesAsync(int orderId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var order = await LoadAsync(db, orderId);
        return await GetShortagesAsync(db, order);
    }

    /// <summary>
    /// Lines whose cost is above the selling price: on a sale, line price below the last purchase cost; on a
    /// purchase, line price (the cost) above the product's selling price. Empty for returns.
    /// </summary>
    public async Task<List<BelowCostInfo>> GetBelowCostLinesAsync(int orderId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var order = await LoadAsync(db, orderId);
        return await GetBelowCostLinesAsync(db, order.OrderType,
            order.Items.Select(i => new OrderLineInput(i.ProductId, i.Quantity, i.UnitPrice)).ToList());
    }

    /// <summary>Same check for lines that are not saved yet (editing a confirmed order).</summary>
    public async Task<List<BelowCostInfo>> GetBelowCostLinesAsync(OrderType type, IReadOnlyList<OrderLineInput> lines)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await GetBelowCostLinesAsync(db, type, lines);
    }

    private static async Task<List<BelowCostInfo>> GetBelowCostLinesAsync(AppDbContext db, OrderType type,
        IReadOnlyList<OrderLineInput> lines)
    {
        if (type is not (OrderType.Sale or OrderType.Purchase))
            return [];

        var result = new List<BelowCostInfo>();
        foreach (var line in lines)
        {
            var product = await db.Products.AsNoTracking().Where(p => p.Id == line.ProductId)
                .Select(p => new { p.Name, p.Price }).FirstAsync();
            var (cost, selling) = type == OrderType.Sale
                ? (await InventoryService.GetLastPurchaseCostAsync(db, line.ProductId), line.UnitPrice)
                : (line.UnitPrice, product.Price);
            if (cost > selling)
                result.Add(new BelowCostInfo(line.ProductId, product.Name, cost, selling));
        }
        return result;
    }

    /// <summary>
    /// Confirms a draft: posts signed ledger rows, recalculates the total and, for purchases,
    /// refreshes the supplier cost price — all in one transaction.
    /// Throws <see cref="ErrorKeys.InsufficientStock"/> unless <paramref name="allowNegativeStock"/>.
    /// </summary>
    /// <param name="allowWalkIn">False: a contact is required (walk-in turned off in settings).</param>
    public async Task<Order> ConfirmAsync(int orderId, bool allowNegativeStock = false, bool allowWalkIn = true)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var order = await LoadAsync(db, orderId);
        if (order.Status != OrderStatus.Draft)
            throw new BusinessRuleException(ErrorKeys.OrderNotDraft, order.OrderNumber);
        if (order.ContactId is null && !allowWalkIn)
            throw new BusinessRuleException(ErrorKeys.ContactRequired);
        if (order.Items.Count == 0)
            throw new BusinessRuleException(ErrorKeys.OrderHasNoItems);
        if (order.Items.Any(i => i.Quantity <= 0))
            throw new BusinessRuleException(ErrorKeys.QuantityNotPositive);

        if (!allowNegativeStock)
        {
            var shortages = await GetShortagesAsync(db, order);
            if (shortages.Count > 0)
                throw new BusinessRuleException(ErrorKeys.InsufficientStock,
                    string.Join(", ", shortages.Select(s => s.ProductName)));
        }

        await PostItemsAsync(db, order);

        if (order.OrderType == OrderType.Purchase)
            await UpdateSupplierCostsAsync(db, order);

        order.TotalAmount = order.Items.Sum(i => i.LineTotal);
        order.Status = OrderStatus.Confirmed;

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        _log.LogInformation("Order {OrderNumber} confirmed (id {OrderId}, {Lines} lines, total {Total}, negative stock allowed: {AllowNegative})",
            order.OrderNumber, order.Id, order.Items.Count, order.TotalAmount, allowNegativeStock);
        return order;
    }

    /// <summary>
    /// Edits a confirmed order in one transaction: the stock it posted is reversed (per product and price, so the
    /// order's net effect is undone), the new lines are posted and <see cref="Order.EditedAt"/> is stamped.
    /// The correction rows keep the order's original posting time, so reports show the order as edited on its day.
    /// Products already on the order keep the unit cost they were posted with; new ones take the last purchase cost.
    /// </summary>
    public async Task<Order> UpdateConfirmedAsync(int orderId, int? contactId, IReadOnlyList<OrderLineInput> lines,
        bool allowNegativeStock = false, bool allowWalkIn = true)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var order = await LoadAsync(db, orderId);
        if (order.Status != OrderStatus.Confirmed)
            throw new BusinessRuleException(ErrorKeys.OrderNotConfirmed, order.OrderNumber);
        if (contactId is null && !allowWalkIn)
            throw new BusinessRuleException(ErrorKeys.ContactRequired);
        await ValidateContactAsync(db, order.OrderType, contactId);
        ValidateDraftLines(lines);
        if (lines.Count == 0)
            throw new BusinessRuleException(ErrorKeys.OrderHasNoItems);
        if (lines.Any(l => l.Quantity <= 0))
            throw new BusinessRuleException(ErrorKeys.QuantityNotPositive);

        var posted = await db.Ledger.AsNoTracking()
            .Where(l => l.DocumentType == order.OrderType && l.DocumentId == order.Id)
            .OrderBy(l => l.Id)
            .ToListAsync();
        var postedAt = posted.Count > 0 ? posted[0].CreatedAt : DateTime.UtcNow;
        var postedCosts = posted.GroupBy(l => l.ProductId).ToDictionary(g => g.Key, g => g.Last().UnitCost);

        // Undo what is still in effect (grouped client-side: decimals are TEXT in SQLite).
        foreach (var group in posted.GroupBy(l => (l.ProductId, l.UnitCost, l.UnitPrice)))
        {
            var net = group.Sum(l => l.Quantity);
            if (net == 0) continue;
            db.Ledger.Add(new LedgerEntry
            {
                ProductId = group.Key.ProductId,
                DocumentType = order.OrderType,
                DocumentId = order.Id,
                Quantity = -net,
                UnitCost = group.Key.UnitCost,
                UnitPrice = group.Key.UnitPrice,
                CreatedAt = postedAt
            });
        }

        order.ContactId = contactId;
        db.OrderItems.RemoveRange(order.Items);
        order.Items = lines.Select(ToItem).ToList();
        await db.SaveChangesAsync();

        if (!allowNegativeStock)
        {
            var shortages = await GetShortagesAsync(db, order);
            if (shortages.Count > 0)
                throw new BusinessRuleException(ErrorKeys.InsufficientStock,
                    string.Join(", ", shortages.Select(s => s.ProductName)));
        }

        await PostItemsAsync(db, order, postedAt,
            order.OrderType == OrderType.Purchase ? null : postedCosts);
        if (order.OrderType == OrderType.Purchase)
            await UpdateSupplierCostsAsync(db, order);

        order.TotalAmount = order.Items.Sum(i => i.LineTotal);
        order.EditedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        _log.LogInformation("Confirmed order {OrderNumber} edited (id {OrderId}, {Lines} lines, total {Total})",
            order.OrderNumber, order.Id, order.Items.Count, order.TotalAmount);
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
        _log.LogInformation("Order {OrderNumber} cancelled (id {OrderId}, {Rows} reversing ledger rows)",
            order.OrderNumber, order.Id, posted.Count);
        return order;
    }

    /// <summary>One signed ledger row per item. Unit cost: purchase = its price, otherwise the last purchase cost.</summary>
    /// <param name="createdAt">Posting time; null = now.</param>
    /// <param name="knownCosts">Unit costs to keep per product (editing a posted order).</param>
    private static async Task PostItemsAsync(AppDbContext db, Order order, DateTime? createdAt = null,
        IReadOnlyDictionary<int, decimal>? knownCosts = null)
    {
        var direction = StockDirection(order.OrderType);
        foreach (var item in order.Items)
        {
            var unitCost = order.OrderType == OrderType.Purchase
                ? item.UnitPrice
                : knownCosts is not null && knownCosts.TryGetValue(item.ProductId, out var known)
                    ? known
                    : await InventoryService.GetLastPurchaseCostAsync(db, item.ProductId);

            db.Ledger.Add(new LedgerEntry
            {
                ProductId = item.ProductId,
                DocumentType = order.OrderType,
                DocumentId = order.Id,
                Quantity = direction * item.Quantity,
                UnitCost = unitCost,
                UnitPrice = item.UnitPrice,
                CreatedAt = createdAt ?? default
            });
        }
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
        // A walk-in purchase records the walk-in (cash) supplier's cost (null contact).
        foreach (var item in order.Items)
        {
            await ProductService.UpsertSupplierCostAsync(db, item.ProductId, order.ContactId, item.UnitPrice);
            await db.SaveChangesAsync(); // so a repeated product in the same order finds the new link
        }

        // ... but never becomes a default supplier (null already means walk-in).
        if (order.ContactId is not int supplierId) return;

        // First supplier a product is bought from becomes its default supplier.
        var productIds = order.Items.Select(i => i.ProductId).Distinct().ToList();
        await db.Products.Where(p => productIds.Contains(p.Id) && p.SupplierId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.SupplierId, supplierId));
    }

    /// <summary>A null contact is the walk-in customer / supplier (or not chosen yet on a draft).</summary>
    private static async Task ValidateContactAsync(AppDbContext db, OrderType type, int? contactId)
    {
        if (contactId is null) return;
        var contact = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contactId)
            ?? throw new BusinessRuleException(ErrorKeys.NotFound);
        if (IsSalesSide(type) && !contact.IsCustomer)
            throw new BusinessRuleException(ErrorKeys.NotACustomer, contact.Name);
        if (!IsSalesSide(type) && !contact.IsSupplier)
            throw new BusinessRuleException(ErrorKeys.NotASupplier, contact.Name);
    }

    private static void ValidateDraftLines(IReadOnlyList<OrderLineInput> lines)
    {
        if (lines.Any(l => l.Quantity < 0))
            throw new BusinessRuleException(ErrorKeys.QuantityNotPositive);
        if (lines.Any(l => l.UnitPrice < 0))
            throw new BusinessRuleException(ErrorKeys.PriceNegative);
    }

    private static OrderItem ToItem(OrderLineInput line) =>
        new() { ProductId = line.ProductId, Quantity = line.Quantity, UnitPrice = line.UnitPrice };

    /// <summary>Per-type prefix + local date + daily sequence: <c>S-261104001</c> = the first sale of 2026-11-04.</summary>
    private static async Task<string> NextOrderNumberAsync(AppDbContext db, OrderType type)
    {
        var prefix = $"{PrefixFor(type)}-{DateTime.Now.ToString("yyMMdd", CultureInfo.InvariantCulture)}";
        var today = await db.Orders.Where(o => o.OrderNumber.StartsWith(prefix))
            .Select(o => o.OrderNumber).ToListAsync();
        var last = today.Select(n => int.TryParse(n.AsSpan(prefix.Length), out var x) ? x : 0).DefaultIfEmpty(0).Max();
        return $"{prefix}{last + 1:D3}";
    }
}

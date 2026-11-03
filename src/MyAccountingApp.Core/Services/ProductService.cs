using Microsoft.EntityFrameworkCore;
using MyAccountingApp.Core.Data;
using MyAccountingApp.Core.Entities;
using MyAccountingApp.Core.Enums;

namespace MyAccountingApp.Core.Services;

/// <summary>A product's supplier; <see cref="SupplierId"/> / <see cref="SupplierName"/> null = the walk-in (cash) supplier.</summary>
public record ProductSupplierLink(int ProductId, int? SupplierId, string? SupplierName);

public class ProductService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>Search by name or barcode (exact barcode matches first).</summary>
    public async Task<List<Product>> SearchAsync(string? text = null, int? categoryId = null, bool includeInactive = false)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.Products.AsNoTracking().Include(p => p.Category)
            .Where(p => includeInactive || p.IsActive);

        if (categoryId is not null)
            query = query.Where(p => p.CategoryId == categoryId);

        if (!string.IsNullOrWhiteSpace(text))
        {
            var t = text.Trim();
            query = query.Where(p => p.Name.Contains(t) || p.Barcode.Contains(t));
            return await query.OrderByDescending(p => p.Barcode == t).ThenBy(p => p.Name).ToListAsync();
        }

        return await query.OrderBy(p => p.Name).ToListAsync();
    }

    /// <summary>
    /// Every (product, supplier) pair: the default supplier and the active product_suppliers links, without duplicates.
    /// Used by the Inventory page's supplier filter.
    /// </summary>
    public async Task<List<ProductSupplierLink>> GetSupplierLinksAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var defaults = await db.Products.AsNoTracking().Where(p => p.SupplierId != null)
            .Select(p => new ProductSupplierLink(p.Id, p.SupplierId, p.Supplier!.Name))
            .ToListAsync();
        var links = await db.ProductSuppliers.AsNoTracking().Where(ps => ps.IsActive)
            .Select(ps => new ProductSupplierLink(ps.ProductId, ps.ContactId, ps.ContactId == null ? null : ps.Contact!.Name))
            .ToListAsync();
        return defaults.Concat(links).DistinctBy(l => (l.ProductId, l.SupplierId)).ToList();
    }

    public async Task<Product?> GetAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Products.AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Supplier)
            .Include(p => p.ProductSuppliers.Where(ps => ps.IsActive)).ThenInclude(ps => ps.Contact)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<Product?> GetByBarcodeAsync(string barcode)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var b = barcode.Trim();
        return await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Barcode == b);
    }

    /// <param name="categoryRequired">
    /// Categories turned on in settings: the product must have one. Off: a null category means "General".
    /// </param>
    /// <param name="openingQuantity">
    /// New products only: stock on hand when the product is added, posted as one <see cref="OrderType.OpeningStock"/>
    /// ledger row at <paramref name="openingCost"/> per unit. Ignored when the product already exists.
    /// </param>
    public async Task<Product> SaveAsync(Product product, bool categoryRequired = true,
        decimal openingQuantity = 0, decimal openingCost = 0)
    {
        product.Name = product.Name.Trim();
        product.Barcode = product.Barcode.Trim();
        if (product.Name.Length == 0)
            throw new BusinessRuleException(ErrorKeys.NameRequired);
        if (product.Barcode.Length == 0)
            throw new BusinessRuleException(ErrorKeys.BarcodeRequired);
        if (product.Price < 0)
            throw new BusinessRuleException(ErrorKeys.PriceNegative);
        if (categoryRequired && product.CategoryId is null)
            throw new BusinessRuleException(ErrorKeys.CategoryRequired);
        var isNew = product.Id == 0;
        if (isNew && openingQuantity < 0)
            throw new BusinessRuleException(ErrorKeys.OpeningQuantityNegative);
        if (isNew && openingCost < 0)
            throw new BusinessRuleException(ErrorKeys.PriceNegative);

        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Products.AnyAsync(p => p.Barcode == product.Barcode && p.Id != product.Id))
            throw new BusinessRuleException(ErrorKeys.BarcodeExists, product.Barcode);

        if (product.SupplierId is int supplierId)
            await EnsureSupplierAsync(db, supplierId);

        // Save scalar fields only; suppliers are managed through SaveSupplierAsync.
        var entity = new Product
        {
            Id = product.Id, Name = product.Name, CategoryId = product.CategoryId, SupplierId = product.SupplierId,
            Barcode = product.Barcode, Price = product.Price, IsActive = product.IsActive, CreatedAt = product.CreatedAt
        };
        await using var tx = await db.Database.BeginTransactionAsync();
        if (isNew) db.Products.Add(entity);
        else db.Products.Update(entity);
        await db.SaveChangesAsync();

        if (isNew && openingQuantity > 0)
        {
            db.Ledger.Add(new LedgerEntry
            {
                ProductId = entity.Id,
                DocumentType = OrderType.OpeningStock,
                DocumentId = entity.Id,
                Quantity = openingQuantity,
                UnitCost = openingCost,
                UnitPrice = openingCost
            });
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync();

        product.Id = entity.Id;
        product.CreatedAt = entity.CreatedAt;
        return product;
    }

    public async Task SetActiveAsync(int id, bool isActive)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Products.Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, isActive));
    }

    /// <summary>Adds or updates the (product, supplier) link and its cost price; re-activates it if it was removed.
    /// <paramref name="contactId"/> null = the walk-in (cash) supplier.</summary>
    public async Task SaveSupplierAsync(int productId, int? contactId, decimal costPrice)
    {
        if (costPrice < 0)
            throw new BusinessRuleException(ErrorKeys.PriceNegative);

        await using var db = await dbFactory.CreateDbContextAsync();
        if (contactId is int id)
            await EnsureSupplierAsync(db, id);
        await UpsertSupplierCostAsync(db, productId, contactId, costPrice);
        await db.SaveChangesAsync();
    }

    public async Task RemoveSupplierAsync(int productId, int? contactId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.ProductSuppliers.Where(ps => ps.ProductId == productId && ps.ContactId == contactId)
            .ExecuteUpdateAsync(s => s.SetProperty(ps => ps.IsActive, false));
        if (contactId is null) return; // walk-in is never a stored default supplier
        await db.Products.Where(p => p.Id == productId && p.SupplierId == contactId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.SupplierId, (int?)null));
    }

    internal static async Task UpsertSupplierCostAsync(AppDbContext db, int productId, int? contactId, decimal costPrice)
    {
        var link = await db.ProductSuppliers.FirstOrDefaultAsync(ps => ps.ProductId == productId && ps.ContactId == contactId);
        if (link is null)
        {
            db.ProductSuppliers.Add(new ProductSupplier { ProductId = productId, ContactId = contactId, CostPrice = costPrice });
        }
        else
        {
            link.CostPrice = costPrice;
            link.IsActive = true;
        }
    }

    private static async Task EnsureSupplierAsync(AppDbContext db, int contactId)
    {
        var contact = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contactId)
            ?? throw new BusinessRuleException(ErrorKeys.NotFound);
        if (!contact.IsSupplier)
            throw new BusinessRuleException(ErrorKeys.NotASupplier, contact.Name);
    }
}

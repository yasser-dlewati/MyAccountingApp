using Microsoft.EntityFrameworkCore;
using MyAccountingApp.Core.Data;
using MyAccountingApp.Core.Entities;

namespace MyAccountingApp.Core.Services;

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

    public async Task<Product> SaveAsync(Product product)
    {
        product.Name = product.Name.Trim();
        product.Barcode = product.Barcode.Trim();
        if (product.Name.Length == 0)
            throw new BusinessRuleException(ErrorKeys.NameRequired);
        if (product.Barcode.Length == 0)
            throw new BusinessRuleException(ErrorKeys.BarcodeRequired);
        if (product.Price < 0)
            throw new BusinessRuleException(ErrorKeys.PriceNegative);
        if (product.CategoryId == 0)
            throw new BusinessRuleException(ErrorKeys.CategoryRequired);

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
        if (entity.Id == 0) db.Products.Add(entity);
        else db.Products.Update(entity);
        await db.SaveChangesAsync();

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

    /// <summary>Adds or updates the (product, supplier) link and its cost price; re-activates it if it was removed.</summary>
    public async Task SaveSupplierAsync(int productId, int contactId, decimal costPrice)
    {
        if (costPrice < 0)
            throw new BusinessRuleException(ErrorKeys.PriceNegative);

        await using var db = await dbFactory.CreateDbContextAsync();
        await EnsureSupplierAsync(db, contactId);
        await UpsertSupplierCostAsync(db, productId, contactId, costPrice);
        await db.SaveChangesAsync();
    }

    public async Task RemoveSupplierAsync(int productId, int contactId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.ProductSuppliers.Where(ps => ps.ProductId == productId && ps.ContactId == contactId)
            .ExecuteUpdateAsync(s => s.SetProperty(ps => ps.IsActive, false));
        await db.Products.Where(p => p.Id == productId && p.SupplierId == contactId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.SupplierId, (int?)null));
    }

    internal static async Task UpsertSupplierCostAsync(AppDbContext db, int productId, int contactId, decimal costPrice)
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

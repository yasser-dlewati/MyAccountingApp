using Microsoft.EntityFrameworkCore;
using MyAccountingApp.Core.Data;
using MyAccountingApp.Core.Entities;

namespace MyAccountingApp.Core.Services;

public class CategoryService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<Category>> GetAllAsync(bool includeInactive = false)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Categories.AsNoTracking()
            .Where(c => includeInactive || c.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<Category> SaveAsync(Category category)
    {
        category.Name = category.Name.Trim();
        if (category.Name.Length == 0)
            throw new BusinessRuleException(ErrorKeys.NameRequired);

        await using var db = await dbFactory.CreateDbContextAsync();
        if (category.Id == 0) db.Categories.Add(category);
        else db.Categories.Update(category);
        await db.SaveChangesAsync();
        return category;
    }

    public async Task SetActiveAsync(int id, bool isActive)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Categories.Where(c => c.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, isActive));
    }
}

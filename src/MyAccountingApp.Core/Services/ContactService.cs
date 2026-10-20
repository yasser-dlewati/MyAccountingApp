using Microsoft.EntityFrameworkCore;
using MyAccountingApp.Core.Data;
using MyAccountingApp.Core.Entities;
using MyAccountingApp.Core.Enums;

namespace MyAccountingApp.Core.Services;

public class ContactService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <param name="role">Customer or Supplier filter; contacts of type Both match either. Null = all.</param>
    public async Task<List<Contact>> SearchAsync(string? text = null, ContactType? role = null, bool includeInactive = false)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.Contacts.AsNoTracking().Where(c => includeInactive || c.IsActive);

        if (role is ContactType.Customer or ContactType.Supplier)
            query = query.Where(c => c.Type == role || c.Type == ContactType.Both);
        else if (role == ContactType.Both)
            query = query.Where(c => c.Type == ContactType.Both);

        if (!string.IsNullOrWhiteSpace(text))
        {
            var t = text.Trim();
            query = query.Where(c => c.Name.Contains(t) || c.Phone.Contains(t));
        }

        return await query.OrderBy(c => c.Name).ToListAsync();
    }

    public async Task<Contact?> GetAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<Contact> SaveAsync(Contact contact)
    {
        contact.Name = contact.Name.Trim();
        contact.Phone = contact.Phone.Trim();
        contact.Address = string.IsNullOrWhiteSpace(contact.Address) ? null : contact.Address.Trim();
        if (contact.Name.Length == 0)
            throw new BusinessRuleException(ErrorKeys.NameRequired);
        if (contact.Phone.Length == 0)
            throw new BusinessRuleException(ErrorKeys.PhoneRequired);

        await using var db = await dbFactory.CreateDbContextAsync();
        if (contact.Id == 0) db.Contacts.Add(contact);
        else db.Contacts.Update(contact);
        await db.SaveChangesAsync();
        return contact;
    }

    public async Task SetActiveAsync(int id, bool isActive)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Contacts.Where(c => c.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, isActive));
    }
}

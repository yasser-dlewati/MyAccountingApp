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

    /// <summary>Normalized phones (see <see cref="NormalizePhone"/>) of every contact, active or not.</summary>
    public async Task<HashSet<string>> GetPhoneKeysAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var phones = await db.Contacts.AsNoTracking().Select(c => c.Phone).ToListAsync();
        return phones.Select(NormalizePhone).Where(p => p.Length > 0).ToHashSet();
    }

    /// <summary>
    /// Adds contacts picked from the phone's address book, all of one <paramref name="type"/>. Skips entries with a
    /// blank name or phone and phones already in the app (or repeated in the batch), compared by <see cref="NormalizePhone"/>.
    /// </summary>
    public async Task<ContactImportResult> ImportAsync(IReadOnlyList<ContactImportInput> items, ContactType type)
    {
        var known = await GetPhoneKeysAsync();
        var added = new List<Contact>();
        foreach (var item in items)
        {
            var name = item.Name?.Trim() ?? "";
            var phone = item.Phone?.Trim() ?? "";
            var key = NormalizePhone(phone);
            if (name.Length == 0 || key.Length == 0 || !known.Add(key)) continue;
            added.Add(new Contact { Name = name, Phone = phone, Type = type });
        }

        if (added.Count > 0)
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            db.Contacts.AddRange(added);
            await db.SaveChangesAsync();
        }
        return new ContactImportResult(added.Count, items.Count - added.Count);
    }

    /// <summary>Digits only (Arabic-Indic digits mapped to Latin), keeping a leading '+': "+963 944-123" → "+963944123".</summary>
    public static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return "";
        var sb = new System.Text.StringBuilder(phone.Length);
        foreach (var ch in phone.Trim())
        {
            if (ch is >= '0' and <= '9') sb.Append(ch);
            else if (ch is >= '\u0660' and <= '\u0669') sb.Append((char)('0' + ch - '\u0660'));
            else if (ch is >= '\u06F0' and <= '\u06F9') sb.Append((char)('0' + ch - '\u06F0'));
            else if (ch == '+' && sb.Length == 0) sb.Append(ch);
        }
        return sb.ToString() == "+" ? "" : sb.ToString();
    }

    public async Task SetActiveAsync(int id, bool isActive)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Contacts.Where(c => c.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, isActive));
    }
}

/// <summary>One name + phone picked from the device's address book.</summary>
public record ContactImportInput(string Name, string Phone);

public record ContactImportResult(int Imported, int Skipped);

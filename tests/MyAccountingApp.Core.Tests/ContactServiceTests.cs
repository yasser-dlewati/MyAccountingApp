using MyAccountingApp.Core.Entities;
using MyAccountingApp.Core.Enums;
using MyAccountingApp.Core.Services;

namespace MyAccountingApp.Core.Tests;

public class ContactServiceTests : IDisposable
{
    private readonly TestDb _t = new();

    public void Dispose() => _t.Dispose();

    [Theory]
    [InlineData("+963 944-123", "+963944123")]
    [InlineData("(0944) 123 456", "0944123456")]
    [InlineData("٠٩٤٤١٢٣", "0944123")]
    [InlineData("۰۹۴۴", "0944")]
    [InlineData("  + ", "")]
    [InlineData(null, "")]
    public void NormalizePhone_keeps_digits_and_leading_plus(string? input, string expected) =>
        Assert.Equal(expected, ContactService.NormalizePhone(input));

    [Fact]
    public async Task Import_adds_new_contacts_with_the_chosen_type()
    {
        var result = await _t.Contacts.ImportAsync(
            [new("Ali", "0944 111"), new(" Sara ", "0944-222")], ContactType.Supplier);

        Assert.Equal(new ContactImportResult(2, 0), result);
        var all = await _t.Contacts.SearchAsync();
        Assert.Equal(["Ali", "Sara"], all.Select(c => c.Name));
        Assert.All(all, c => Assert.Equal(ContactType.Supplier, c.Type));
        Assert.All(all, c => Assert.True(c.IsActive));
    }

    [Fact]
    public async Task Import_skips_existing_duplicate_and_blank_entries()
    {
        await _t.Contacts.SaveAsync(new Contact { Name = "Old", Phone = "+963 944 111", IsActive = false });

        var result = await _t.Contacts.ImportAsync(
        [
            new("Same phone", "+963944111"),   // already in the app (inactive counts too)
            new("New", "0955 222"),
            new("New again", "0955-222"),      // repeated in the batch
            new("", "0966"),                   // no name
            new("No phone", "  ")
        ], ContactType.Customer);

        Assert.Equal(new ContactImportResult(1, 4), result);
        var all = await _t.Contacts.SearchAsync(includeInactive: true);
        Assert.Equal(["New", "Old"], all.Select(c => c.Name));
        Assert.Contains("0955222", await _t.Contacts.GetPhoneKeysAsync());
    }
}

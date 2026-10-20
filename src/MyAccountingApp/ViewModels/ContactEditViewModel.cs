using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAccountingApp.AppServices;
using MyAccountingApp.Core.Entities;
using Contact = MyAccountingApp.Core.Entities.Contact;
using MyAccountingApp.Core.Enums;
using MyAccountingApp.Core.Services;
using MyAccountingApp.Helpers;
using MyAccountingApp.Localization;

namespace MyAccountingApp.ViewModels;

public partial class ContactEditViewModel : BaseViewModel, IQueryAttributable
{
    private readonly ContactService _contacts;
    private Contact _contact = new();

    public ContactEditViewModel(ContactService contacts, DialogService dialogs) : base(dialogs)
    {
        _contacts = contacts;
        Type = TypeOptions[0];
        Title = LocalizationService.Get("Contact_New");
    }

    public List<Option<ContactType>> TypeOptions { get; } = EnumLabels.Options<ContactType>(EnumLabels.Of);

    [ObservableProperty] public partial string Title { get; set; }
    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string Phone { get; set; } = "";
    [ObservableProperty] public partial string? Address { get; set; }
    [ObservableProperty] public partial Option<ContactType> Type { get; set; }
    [ObservableProperty] public partial bool IsActive { get; set; } = true;

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("id", out var value) || value is not int id || id == 0) return;
        await RunAsync(async () =>
        {
            _contact = await _contacts.GetAsync(id) ?? throw new BusinessRuleException(ErrorKeys.NotFound);
            Title = _contact.Name;
            Name = _contact.Name;
            Phone = _contact.Phone;
            Address = _contact.Address;
            Type = TypeOptions.First(o => o.Value == _contact.Type);
            IsActive = _contact.IsActive;
        });
    }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        _contact.Name = Name;
        _contact.Phone = Phone;
        _contact.Address = Address;
        _contact.Type = Type.Value;
        _contact.IsActive = IsActive;
        await _contacts.SaveAsync(_contact);
        await GoBackAsync();
    });
}

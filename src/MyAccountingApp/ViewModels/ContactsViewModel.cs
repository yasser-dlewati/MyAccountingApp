using System.Collections.ObjectModel;
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

public partial class ContactsViewModel : BaseViewModel
{
    private readonly ContactService _contacts;

    public ContactsViewModel(ContactService contacts, DialogService dialogs) : base(dialogs)
    {
        _contacts = contacts;
        RoleFilter = RoleOptions[0];
    }

    public ObservableCollection<ContactRow> Items { get; } = [];

    public List<Option<ContactType?>> RoleOptions { get; } =
    [
        new(LocalizationService.Get("Common_All"), null),
        new(LocalizationService.Get("ContactType_Customer"), ContactType.Customer),
        new(LocalizationService.Get("ContactType_Supplier"), ContactType.Supplier)
    ];

    [ObservableProperty]
    public partial Option<ContactType?> RoleFilter { get; set; }

    [ObservableProperty]
    public partial string? SearchText { get; set; }

    [ObservableProperty]
    public partial bool ShowInactive { get; set; }

    partial void OnRoleFilterChanged(Option<ContactType?> value) => LoadCommand.Execute(null);
    partial void OnShowInactiveChanged(bool value) => LoadCommand.Execute(null);

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        var list = await _contacts.SearchAsync(SearchText, RoleFilter?.Value, ShowInactive);
        Items.Clear();
        foreach (var c in list) Items.Add(new ContactRow(c));
    });

    [RelayCommand]
    private Task AddAsync() => GoToAsync(Routes.ContactEdit, new Dictionary<string, object> { ["id"] = 0 });

    [RelayCommand]
    private Task OpenAsync(ContactRow row) =>
        GoToAsync(Routes.ContactEdit, new Dictionary<string, object> { ["id"] = row.Contact.Id });
}

public record ContactRow(Contact Contact)
{
    public string TypeLabel => EnumLabels.Of(Contact.Type);
    public string Subtitle => $"{Contact.Phone} · {TypeLabel}";
    public double Opacity => Contact.IsActive ? 1 : 0.45;
}

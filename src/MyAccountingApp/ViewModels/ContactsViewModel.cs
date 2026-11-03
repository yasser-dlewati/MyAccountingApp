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
        _inactiveChip = new FilterChip(LocalizationService.Get("Common_ShowInactive"), null, Colors.Gray);
        _roleChips =
        [
            new(LocalizationService.Get("Common_All"), null) { IsSelected = true },
            new(LocalizationService.Get("ContactType_Customer"), ContactType.Customer),
            new(LocalizationService.Get("ContactType_Supplier"), ContactType.Supplier)
        ];
        Chips = [.. _roleChips, _inactiveChip];
    }

    private readonly List<FilterChip> _roleChips;
    // Last pill: an on/off toggle, separate from the single-choice role pills.
    private readonly FilterChip _inactiveChip;

    public ObservableCollection<ContactRow> Items { get; } = [];

    /// <summary>Role pills (All / Customers / Suppliers, with counts) followed by the Show inactive toggle.</summary>
    public List<FilterChip> Chips { get; }

    private ContactType? RoleFilter => (ContactType?)_roleChips.First(c => c.IsSelected).Value;

    [ObservableProperty]
    public partial string? SearchText { get; set; }

    [ObservableProperty]
    public partial bool ShowInactive { get; set; }

    partial void OnShowInactiveChanged(bool value)
    {
        _inactiveChip.IsSelected = value;
        LoadCommand.Execute(null);
    }

    [RelayCommand]
    private void SelectChip(FilterChip chip)
    {
        if (ReferenceEquals(chip, _inactiveChip))
        {
            ShowInactive = !ShowInactive;
            return;
        }
        if (chip.IsSelected) return;
        FilterChip.Select(_roleChips, chip);
        LoadCommand.Execute(null);
    }

    /// <summary>Loads every role (to count them; "Both" counts on each side) and lists the selected one.</summary>
    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        var list = await _contacts.SearchAsync(SearchText, includeInactive: ShowInactive);
        bool Matches(Contact c, ContactType? role) => role switch
        {
            ContactType.Customer => c.IsCustomer,
            ContactType.Supplier => c.IsSupplier,
            _ => true
        };
        foreach (var chip in _roleChips)
            chip.Count = Formats.Quantity(list.Count(c => Matches(c, (ContactType?)chip.Value)));

        var role = RoleFilter;
        Items.Clear();
        foreach (var c in list.Where(c => Matches(c, role))) Items.Add(new ContactRow(c));
    });

    [RelayCommand]
    private Task AddAsync() => GoToAsync(Routes.ContactEdit, new Dictionary<string, object> { ["id"] = 0 });

    [RelayCommand]
    private Task ImportAsync() => GoToAsync(Routes.ContactImport);

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

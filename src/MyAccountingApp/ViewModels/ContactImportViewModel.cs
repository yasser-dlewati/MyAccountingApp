using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MyAccountingApp.AppServices;
using MyAccountingApp.Core.Enums;
using MyAccountingApp.Core.Services;
using MyAccountingApp.Helpers;
using MyAccountingApp.Localization;

namespace MyAccountingApp.ViewModels;

/// <summary>Contacts → Import from phone: a checklist of the address book, imported as one contact type.</summary>
public partial class ContactImportViewModel(ContactService contacts, PhoneContactsService phone, DialogService dialogs,
    ILogger<ContactImportViewModel> logger) : BaseViewModel(dialogs)
{
    private List<PhoneContactRow> _all = [];
    private bool _loaded;

    public ObservableCollection<PhoneContactRow> Items { get; } = [];

    public List<Option<ContactType>> TypeOptions { get; } =
        Enum.GetValues<ContactType>().Select(t => new Option<ContactType>(EnumLabels.Of(t), t)).ToList();

    [ObservableProperty]
    public partial Option<ContactType> Type { get; set; } = null!;

    [ObservableProperty]
    public partial string? SearchText { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportLabel), nameof(SelectedLabel))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    public partial int SelectedCount { get; set; }

    public string ImportLabel => LocalizationService.Format("Import_Button", Formats.Quantity(SelectedCount));
    public string SelectedLabel => LocalizationService.Format("Import_Selected", Formats.Quantity(SelectedCount));

    /// <summary>True when every selectable row in the current search is ticked.</summary>
    [ObservableProperty]
    public partial bool AllSelected { get; set; }

    partial void OnSearchTextChanged(string? value) => ApplyFilter();

    /// <summary>Reads the address book once (asks for the permission); closes the page when it is denied.</summary>
    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        if (_loaded) return;
        Type ??= TypeOptions[0];
        var read = await phone.GetAllAsync();
        if (read is null)
        {
            await GoBackAsync();
            return;
        }
        var known = await contacts.GetPhoneKeysAsync();
        _all = read.Select(c => new PhoneContactRow(c, known.Contains(ContactService.NormalizePhone(c.Phone)), OnRowChanged)).ToList();
        _loaded = true;
        ApplyFilter();
    });

    private void ApplyFilter()
    {
        var t = SearchText?.Trim();
        Items.Clear();
        foreach (var r in _all.Where(r => string.IsNullOrEmpty(t)
                     || r.Input.Name.Contains(t, StringComparison.CurrentCultureIgnoreCase)
                     || r.Input.Phone.Contains(t)))
            Items.Add(r);
        IsEmpty = _loaded && Items.Count == 0;
        UpdateCounts();
    }

    private void OnRowChanged() => UpdateCounts();

    private void UpdateCounts()
    {
        SelectedCount = _all.Count(r => r.IsSelected);
        var selectable = Items.Where(r => r.CanSelect).ToList();
        AllSelected = selectable.Count > 0 && selectable.All(r => r.IsSelected);
    }

    /// <summary>Ticks (or unticks, when all are ticked) every selectable row in the current search.</summary>
    [RelayCommand]
    private void ToggleAll()
    {
        var select = !AllSelected;
        foreach (var r in Items.Where(r => r.CanSelect)) r.SetSelected(select);
        UpdateCounts();
    }

    [RelayCommand]
    private void Toggle(PhoneContactRow row)
    {
        if (row.CanSelect) row.IsSelected = !row.IsSelected;
    }

    private bool CanImport() => SelectedCount > 0;

    [RelayCommand(CanExecute = nameof(CanImport))]
    private Task ImportAsync() => RunAsync(async () =>
    {
        var picked = _all.Where(r => r.IsSelected).Select(r => r.Input).ToList();
        var result = await contacts.ImportAsync(picked, Type.Value);
        logger.LogInformation("Imported {Imported} phone contacts as {Type}, skipped {Skipped}", result.Imported, Type.Value, result.Skipped);
        await Dialogs.AlertAsync(LocalizationService.Format("Import_Done",
            Formats.Quantity(result.Imported), Formats.Quantity(result.Skipped)));
        await GoBackAsync();
    });
}

public partial class PhoneContactRow(ContactImportInput input, bool exists, Action changed) : ObservableObject
{
    public ContactImportInput Input { get; } = input;
    public bool Exists { get; } = exists;
    public bool CanSelect => !Exists;
    public double Opacity => Exists ? 0.45 : 1;
    public string Subtitle => Exists ? $"{Input.Phone} · {LocalizationService.Get("Import_AlreadyAdded")}" : Input.Phone;

    /// <summary>First letter for the avatar circle.</summary>
    public string Initial => Input.Name.Length > 0 ? char.ToUpper(Input.Name[0]).ToString() : "?";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        if (Exists && value) IsSelected = false;
        else if (!_bulk) changed();
    }

    private bool _bulk;

    /// <summary>Select without a count update per row (the caller updates once).</summary>
    public void SetSelected(bool value)
    {
        _bulk = true;
        try { IsSelected = value; }
        finally { _bulk = false; }
    }
}

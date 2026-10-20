using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAccountingApp.AppServices;
using MyAccountingApp.Core.Entities;
using MyAccountingApp.Core.Services;
using MyAccountingApp.Localization;

namespace MyAccountingApp.ViewModels;

public partial class CategoriesViewModel(CategoryService categories, DialogService dialogs) : BaseViewModel(dialogs)
{
    public ObservableCollection<Category> Items { get; } = [];

    [ObservableProperty]
    public partial bool ShowInactive { get; set; }

    partial void OnShowInactiveChanged(bool value) => LoadCommand.Execute(null);

    [RelayCommand]
    private Task LoadAsync() => RunAsync(async () =>
    {
        var list = await categories.GetAllAsync(ShowInactive);
        Items.Clear();
        foreach (var c in list) Items.Add(c);
    });

    [RelayCommand]
    private async Task AddAsync()
    {
        var name = await Dialogs.PromptAsync(LocalizationService.Get("Category_New"));
        if (name is null) return;
        await RunAsync(() => categories.SaveAsync(new Category { Name = name }));
        await LoadAsync();
    }

    [RelayCommand]
    private async Task EditAsync(Category category)
    {
        var rename = LocalizationService.Get("Common_Rename");
        var toggle = LocalizationService.Get(category.IsActive ? "Common_Deactivate" : "Common_Activate");
        var choice = await Dialogs.ChooseAsync(category.Name, rename, toggle);

        if (choice == rename)
        {
            var name = await Dialogs.PromptAsync(LocalizationService.Get("Category_Edit"), category.Name);
            if (name is null) return;
            await RunAsync(() => categories.SaveAsync(new Category { Id = category.Id, Name = name, IsActive = category.IsActive }));
        }
        else if (choice == toggle)
        {
            await RunAsync(() => categories.SetActiveAsync(category.Id, !category.IsActive));
        }
        else return;

        await LoadAsync();
    }
}

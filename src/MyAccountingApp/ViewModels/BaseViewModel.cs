using CommunityToolkit.Mvvm.ComponentModel;
using MyAccountingApp.AppServices;
using MyAccountingApp.Core.Services;

namespace MyAccountingApp.ViewModels;

public abstract partial class BaseViewModel(DialogService dialogs) : ObservableObject
{
    protected DialogService Dialogs { get; } = dialogs;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Runs an action with the busy flag set; business-rule errors are shown to the user.</summary>
    protected async Task RunAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await action();
        }
        catch (BusinessRuleException ex)
        {
            await Dialogs.ShowErrorAsync(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Parameters are single-use (not re-applied when navigating back to the page).</summary>
    protected static Task GoToAsync(string route, IDictionary<string, object>? parameters = null) =>
        parameters is null
            ? Shell.Current.GoToAsync(route)
            : Shell.Current.GoToAsync(route, new ShellNavigationQueryParameters(parameters));

    protected static Task GoBackAsync() => Shell.Current.GoToAsync("..");
}

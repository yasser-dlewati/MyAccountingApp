using MyAccountingApp.ViewModels;

namespace MyAccountingApp.Views;

public partial class ContactsPage : ContentPage
{
	private readonly ContactsViewModel _viewModel;

	public ContactsPage(ContactsViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = _viewModel = viewModel;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		_viewModel.LoadCommand.Execute(null);
	}
}

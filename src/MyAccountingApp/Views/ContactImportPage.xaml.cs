using MyAccountingApp.ViewModels;

namespace MyAccountingApp.Views;

public partial class ContactImportPage : ContentPage
{
	private readonly ContactImportViewModel _viewModel;

	public ContactImportPage(ContactImportViewModel viewModel)
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

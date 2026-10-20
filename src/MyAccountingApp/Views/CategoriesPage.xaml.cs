using MyAccountingApp.ViewModels;

namespace MyAccountingApp.Views;

public partial class CategoriesPage : ContentPage
{
	private readonly CategoriesViewModel _viewModel;

	public CategoriesPage(CategoriesViewModel viewModel)
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

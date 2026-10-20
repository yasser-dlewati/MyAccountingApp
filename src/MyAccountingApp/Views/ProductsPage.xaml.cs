using MyAccountingApp.ViewModels;

namespace MyAccountingApp.Views;

public partial class ProductsPage : ContentPage
{
	private readonly ProductsViewModel _viewModel;

	public ProductsPage(ProductsViewModel viewModel)
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

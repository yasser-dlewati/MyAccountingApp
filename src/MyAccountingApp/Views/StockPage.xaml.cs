using MyAccountingApp.ViewModels;

namespace MyAccountingApp.Views;

public partial class StockPage : ContentPage
{
	private readonly StockViewModel _viewModel;

	public StockPage(StockViewModel viewModel)
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

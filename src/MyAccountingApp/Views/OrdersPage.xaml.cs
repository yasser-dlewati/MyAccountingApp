using MyAccountingApp.ViewModels;

namespace MyAccountingApp.Views;

public partial class OrdersPage : ContentPage
{
	private readonly OrdersViewModel _viewModel;

	public OrdersPage(OrdersViewModel viewModel)
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

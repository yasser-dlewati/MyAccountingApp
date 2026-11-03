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

	// Pop = back from an opened order; tab switches arrive as Replace / PopToRoot.
	protected override void OnNavigatedTo(NavigatedToEventArgs args)
	{
		base.OnNavigatedTo(args);
		_viewModel.OnNavigatedTo(returning: args.NavigationType == NavigationType.Pop);
	}
}

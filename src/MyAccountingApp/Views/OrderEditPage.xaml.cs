using MyAccountingApp.ViewModels;

namespace MyAccountingApp.Views;

public partial class OrderEditPage : ContentPage
{
	public OrderEditPage(OrderEditViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
	}
}

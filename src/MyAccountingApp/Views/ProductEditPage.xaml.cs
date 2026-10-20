using MyAccountingApp.ViewModels;

namespace MyAccountingApp.Views;

public partial class ProductEditPage : ContentPage
{
	public ProductEditPage(ProductEditViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
	}
}

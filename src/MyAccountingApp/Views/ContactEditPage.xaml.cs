using MyAccountingApp.ViewModels;

namespace MyAccountingApp.Views;

public partial class ContactEditPage : ContentPage
{
	public ContactEditPage(ContactEditViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
	}
}

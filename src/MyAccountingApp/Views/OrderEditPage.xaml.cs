using MyAccountingApp.ViewModels;

namespace MyAccountingApp.Views;

public partial class OrderEditPage : ContentPage
{
	private readonly OrderEditViewModel _viewModel;

	public OrderEditPage(OrderEditViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = _viewModel = viewModel;
		// Leaving saves the draft (or drops an empty one): toolbar arrow and system back both go through BackCommand.
		Shell.SetBackButtonBehavior(this, new BackButtonBehavior { Command = viewModel.BackCommand });
	}

	protected override bool OnBackButtonPressed()
	{
		_viewModel.BackCommand.Execute(null);
		return true;
	}
}

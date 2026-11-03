using System.Collections;
using System.Windows.Input;

namespace MyAccountingApp.Controls;

/// <summary>Filter pills (<see cref="Helpers.FilterChip"/>) used for the status / role filters on list pages.</summary>
public partial class FilterChipBar : ContentView
{
    public static readonly BindableProperty ItemsSourceProperty =
        BindableProperty.Create(nameof(ItemsSource), typeof(IEnumerable), typeof(FilterChipBar));

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(FilterChipBar));

    public FilterChipBar() => InitializeComponent();

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>Runs with the tapped <see cref="Helpers.FilterChip"/>.</summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }
}

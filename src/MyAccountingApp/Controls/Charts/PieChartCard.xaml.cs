namespace MyAccountingApp.Controls.Charts;

/// <summary>A donut + legend card; the page binds <see cref="Title"/> and <see cref="Data"/>.</summary>
public partial class PieChartCard : ContentView
{
	public static readonly BindableProperty TitleProperty = BindableProperty.Create(
		nameof(Title), typeof(string), typeof(PieChartCard), null);

	public static readonly BindableProperty DataProperty = BindableProperty.Create(
		nameof(Data), typeof(PieData), typeof(PieChartCard), PieData.Empty);

	public string? Title
	{
		get => (string?)GetValue(TitleProperty);
		set => SetValue(TitleProperty, value);
	}

	public PieData Data
	{
		get => (PieData)GetValue(DataProperty);
		set => SetValue(DataProperty, value);
	}

	public PieChartCard()
	{
		InitializeComponent();
		// Inner bindings read this control's properties; the page's BindingContext feeds Title / Data.
		Root.BindingContext = this;
	}
}

/// <summary>A donut's segments + its center text (the compact total).</summary>
public record PieData(IReadOnlyList<ChartSlice> Slices, string Total)
{
	public static readonly PieData Empty = new([], "");

	public bool IsEmpty => Slices.Count == 0;
	public bool HasData => !IsEmpty;
}

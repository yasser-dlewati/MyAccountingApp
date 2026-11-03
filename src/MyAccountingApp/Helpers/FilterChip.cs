using CommunityToolkit.Mvvm.ComponentModel;

namespace MyAccountingApp.Helpers;

/// <summary>
/// One pill of a <see cref="Controls.FilterChipBar"/>: label, optional colored dot (e.g. the status color) and count.
/// <see cref="Value"/> is what the page filters by (null = "All"); the VM sets <see cref="IsSelected"/>.
/// </summary>
public partial class FilterChip(string label, object? value, Color? dot = null) : ObservableObject
{
    public string Label { get; } = label;
    public object? Value { get; } = value;
    public Color? Dot { get; } = dot;
    public bool HasDot => Dot is not null;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCount))]
    public partial string? Count { get; set; }

    public bool HasCount => Count is not null;

    /// <summary>Marks <paramref name="chosen"/> as the only selected chip of a single-choice bar.</summary>
    public static void Select(IEnumerable<FilterChip> chips, FilterChip chosen)
    {
        foreach (var chip in chips) chip.IsSelected = ReferenceEquals(chip, chosen);
    }
}

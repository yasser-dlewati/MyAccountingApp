namespace MyAccountingApp.Helpers;

/// <summary>Picker item: Picker shows ToString(), so no ItemDisplayBinding is needed.</summary>
public record Option<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

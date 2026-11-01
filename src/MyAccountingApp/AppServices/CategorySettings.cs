namespace MyAccountingApp.AppServices;

/// <summary>
/// Settings → Categories switch, saved in Preferences. On: every product must have a category.
/// Off: the category field is hidden and new products go under the built-in "General" (category_id null).
/// </summary>
public static class CategorySettings
{
    private const string PreferenceKey = "use_categories";

    public static bool Enabled
    {
        get => Preferences.Default.Get(PreferenceKey, true);
        set => Preferences.Default.Set(PreferenceKey, value);
    }
}

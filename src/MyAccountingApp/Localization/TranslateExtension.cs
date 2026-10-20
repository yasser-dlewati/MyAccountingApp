namespace MyAccountingApp.Localization;

/// <summary>
/// {loc:Translate Key} — resolves once when the page is built. Changing the language rebuilds
/// the Shell (App.ReloadShell), so no live binding is needed.
/// </summary>
[ContentProperty(nameof(Key))]
[AcceptEmptyServiceProvider]
public class TranslateExtension : IMarkupExtension<string>
{
    public string Key { get; set; } = "";

    public string ProvideValue(IServiceProvider serviceProvider) => LocalizationService.Get(Key);

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}

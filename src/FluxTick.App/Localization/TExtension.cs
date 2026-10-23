using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace FluxTick.App.Localization;

/// <summary>
/// A XAML markup extension that shows the text for a key and follows the language as it changes:
/// <c>Text="{loc:T Settings_Theme}"</c>.
/// </summary>
/// <param name="key">
/// The text key.
/// </param>
public sealed class TExtension(string key) : MarkupExtension
{
    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay };
}

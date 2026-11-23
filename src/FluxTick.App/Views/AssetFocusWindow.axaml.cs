using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace FluxTick.App.Views;

/// <summary>
/// Shows one instrument on a window that fills the screen. The window shares the row of the list, so it is live without
/// a feed of its own; Esc, a double-click or the close button ends it.
/// </summary>
public sealed partial class AssetFocusWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AssetFocusWindow"/> class.
    /// </summary>
    public AssetFocusWindow()
    {
        InitializeComponent();
        KeyDown += OnWindowKeyDown;
        DoubleTapped += OnWindowDoubleTapped;
        ApplySizes(Height);
    }

    /// <summary>
    /// Sizes the text again when the window changes height.
    /// </summary>
    /// <param name="e">The event data.</param>
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ApplySizes(e.NewSize.Height);
    }

    /// <summary>
    /// Gives the keyboard focus to the window, so that Esc works at once.
    /// </summary>
    /// <param name="e">The event data.</param>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Focus();
    }

    /// <summary>
    /// Sets the font sizes from the height of the window.
    /// </summary>
    /// <param name="height">The height of the window.</param>
    private void ApplySizes(double height)
    {
        var detail = AssetFocusRules.DetailFontSize(height);
        var name = AssetFocusRules.NameFontSize(height);
        var change = AssetFocusRules.ChangeFontSize(height);
        Set("PriceText", AssetFocusRules.PriceFontSize(height));
        Set("ChangeText", change);
        Set("StatusText", change);
        Set("AmountText", detail * 1.4);
        Set("BasisText", detail);
        Set("NameText", name);
        Set("SymbolText", name * 0.7);
        Set("ProviderText", detail);

        foreach (var block in this.GetVisualDescendants().OfType<TextBlock>().Where(b => b.Classes.Contains("detail")))
        {
            block.FontSize = detail;
        }
    }

    /// <summary>
    /// Sets the font size of a named text.
    /// </summary>
    /// <param name="name">The name of the text.</param>
    /// <param name="size">The font size.</param>
    private void Set(string name, double size)
    {
        if (this.FindControl<TextBlock>(name) is { } block)
        {
            block.FontSize = size;
        }
    }

    /// <summary>
    /// Closes the window on Esc.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">The event data.</param>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    /// <summary>
    /// Closes the window on a double-click.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">The event data.</param>
    private void OnWindowDoubleTapped(object? sender, TappedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Closes the window.
    /// </summary>
    /// <param name="sender">The close button.</param>
    /// <param name="e">The event data.</param>
    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

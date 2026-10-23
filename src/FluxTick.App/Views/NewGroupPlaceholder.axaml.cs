using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FluxTick.App.Views;

/// <summary>
/// The dashed "New group" placeholder. Clicking it, or dropping a card on it in the card layout, starts a new group.
/// </summary>
public sealed partial class NewGroupPlaceholder : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NewGroupPlaceholder"/> class.
    /// </summary>
    public NewGroupPlaceholder()
    {
        InitializeComponent();
        Inner.Click += (_, e) => Click?.Invoke(this, e);
    }

    /// <summary>
    /// Raised when the placeholder is clicked.
    /// </summary>
    public event EventHandler<RoutedEventArgs>? Click;
}

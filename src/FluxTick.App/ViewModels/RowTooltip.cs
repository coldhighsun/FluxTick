using CommunityToolkit.Mvvm.ComponentModel;

namespace FluxTick.App.ViewModels;

/// <summary>
/// The content of the tooltip of a row or card. A view shows it through a template, which stays bound after the tooltip
/// closed, so the text is announced only while the tooltip is open (<see cref="IsOpen"/>); a price that comes in while it
/// is closed costs nothing.
/// </summary>
/// <param name="row">
/// The row the tooltip belongs to.
/// </param>
public sealed class RowTooltip(AssetRowViewModel row) : ObservableObject
{
    /// <summary>
    /// Whether the tooltip is open.
    /// </summary>
    private bool _isOpen;

    /// <summary>
    /// Gets the text of the tooltip.
    /// </summary>
    public string Text => row.Tooltip;

    /// <summary>
    /// Gets or sets a value indicating whether the tooltip is open. The view sets it as the tooltip opens and closes; the
    /// text is announced when it opens, as it may have changed while the tooltip was closed.
    /// </summary>
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (_isOpen == value)
            {
                return;
            }

            _isOpen = value;
            if (value)
            {
                OnPropertyChanged(nameof(Text));
            }
        }
    }

    /// <summary>
    /// Announces that the text changed, when the tooltip is open to show it.
    /// </summary>
    public void Refresh()
    {
        if (_isOpen)
        {
            OnPropertyChanged(nameof(Text));
        }
    }
}

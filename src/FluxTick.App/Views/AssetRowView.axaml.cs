using Avalonia.Controls;

namespace FluxTick.App.Views;

/// <summary>
/// One watchlist row. Plays a short flash on every price change.
/// </summary>
public sealed partial class AssetRowView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AssetRowView"/> class.
    /// </summary>
    public AssetRowView()
    {
        InitializeComponent();
        _ = new RowAnimator(this);
    }
}

using Avalonia.Controls;

namespace FluxTick.App.Views;

/// <summary>
/// One fixed-size card of the card layout. Plays a short flash on every price change.
/// </summary>
public sealed partial class AssetCardView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AssetCardView"/> class.
    /// </summary>
    public AssetCardView()
    {
        InitializeComponent();
        _ = new FlashAnimator(this);
    }
}

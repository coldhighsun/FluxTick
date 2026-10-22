using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using FluxTick.App.ViewModels;

namespace FluxTick.App.Views;

/// <summary>
/// One watchlist row. Plays a short flash on every price change.
/// </summary>
public sealed partial class AssetRowView : UserControl
{
    /// <summary>
    /// The flash animation: a quick tint that fades out.
    /// </summary>
    private static readonly Animation FlashAnimation = new()
    {
        Duration = TimeSpan.FromMilliseconds(700),
        Easing = new CubicEaseOut(),
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0.22) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 0.0) } },
        },
    };

    /// <summary>
    /// The view model whose changes are being observed.
    /// </summary>
    private AssetRowViewModel? _observed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetRowView"/> class.
    /// </summary>
    public AssetRowView() => InitializeComponent();

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_observed is not null)
        {
            _observed.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _observed = DataContext as AssetRowViewModel;
        if (_observed is not null)
        {
            _observed.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    /// <summary>
    /// Plays the flash when the view model announces a new tick.
    /// </summary>
    /// <param name="sender">
    /// The view model.
    /// </param>
    /// <param name="e">
    /// The property change data.
    /// </param>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AssetRowViewModel.FlashVersion) || _observed is null)
        {
            return;
        }

        var flash = this.FindControl<Border>("Flash");
        if (flash is null)
        {
            return;
        }

        var key = _observed.FlashUp ? "UpBrush" : "DownBrush";
        if (Application.Current?.TryGetResource(key, ActualThemeVariant, out var brush) == true && brush is IBrush typed)
        {
            flash.Background = typed;
            _ = FlashAnimation.RunAsync(flash);
        }
    }
}

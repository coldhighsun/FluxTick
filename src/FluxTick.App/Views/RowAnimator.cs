using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using FluxTick.App.ViewModels;

namespace FluxTick.App.Views;

/// <summary>
/// The rules that decide whether a flash starts.
/// </summary>
internal static class RowAnimationPolicy
{
    /// <summary>
    /// Tells whether a new flash of the price should start.
    /// </summary>
    /// <param name="enabled">
    /// Whether the flash is turned on.
    /// </param>
    /// <param name="visible">
    /// Whether the view can be seen; a hidden one has nobody to show the flash to.
    /// </param>
    /// <param name="playing">
    /// Whether a flash of the same view is still playing; it is not restarted, which caps the work per row however
    /// fast the prices arrive.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a flash should start.
    /// </returns>
    public static bool ShouldPlay(bool enabled, bool visible, bool playing) => enabled && visible && !playing;

    /// <summary>
    /// Tells whether the flash of an alert that fired should start. It does not depend on the setting of the flash on
    /// price changes, and a new alert starts over even when the flash of an earlier one is still playing.
    /// </summary>
    /// <param name="visible">
    /// Whether the view can be seen; a hidden one has nobody to show the flash to.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a flash should start.
    /// </returns>
    public static bool ShouldPlayAlert(bool visible) => visible;
}

/// <summary>
/// Plays the two flashes of a row or card whenever its row view model announces them. A change of the price rolls the
/// number: the old price (the control named <c>PriceOld</c>) slides out of the clipped box it shares with the new one
/// (<c>PriceText</c>), up when the price rose and down when it fell, while the new price slides in from the other side;
/// both have a translate transform. An alert that fired tints the control named <c>Flash</c>, which covers the whole row
/// or card, in several pulses over a few seconds. It only listens while the view is part of the visual tree, so a view
/// that is taken out of the tree does not stay reachable from a long-lived view model. A view that cannot be seen plays
/// nothing, a roll of the price that is playing is not restarted by the next tick, and turning the flash of the price
/// off stops it at once.
/// </summary>
internal sealed class RowAnimator
{
    /// <summary>
    /// How many pulses the flash of an alert has; with the length of a pulse that makes about six seconds.
    /// </summary>
    private const ulong AlertPulses = 5;

    /// <summary>
    /// How long the roll of the price takes.
    /// </summary>
    private static readonly TimeSpan RollDuration = TimeSpan.FromMilliseconds(350);

    /// <summary>
    /// One pulse of the flash of an alert: the tint swells and fades out.
    /// </summary>
    private static readonly Animation AlertAnimation = new()
    {
        Duration = TimeSpan.FromMilliseconds(1200),
        IterationCount = new IterationCount(AlertPulses),
        Easing = new SineEaseInOut(),
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0.0) } },
            new KeyFrame { Cue = new Cue(0.35), Setters = { new Setter(Visual.OpacityProperty, 0.4) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 0.0) } },
        },
    };

    /// <summary>
    /// The view that hosts the flashing controls.
    /// </summary>
    private readonly UserControl _view;

    /// <summary>
    /// The view model whose changes are being observed.
    /// </summary>
    private AssetRowViewModel? _observed;

    /// <summary>
    /// Cancels the roll of the price that is playing, or <see langword="null"/> when none is.
    /// </summary>
    private CancellationTokenSource? _playingPrice;

    /// <summary>
    /// Cancels the flash of an alert that is playing, or <see langword="null"/> when none is.
    /// </summary>
    private CancellationTokenSource? _playingAlert;

    /// <summary>
    /// Initializes a new instance of the <see cref="RowAnimator"/> class and wires it to the view.
    /// </summary>
    /// <param name="view">
    /// The view to animate. Its data context is expected to be an <see cref="AssetRowViewModel"/>.
    /// </param>
    public RowAnimator(UserControl view)
    {
        _view = view;
        view.DataContextChanged += (_, _) => Observe(view.IsAttachedToVisualTree() ? view.DataContext as AssetRowViewModel : null);
        view.AttachedToVisualTree += (_, _) => Observe(view.DataContext as AssetRowViewModel);
        view.DetachedFromVisualTree += (_, _) => Observe(null);
    }

    /// <summary>
    /// Builds the animation of one of the two numbers of the roll: it slides from one offset to another while it fades in
    /// or out. The last values are held until the roll is reset, so that the number does not jump back for a frame.
    /// </summary>
    /// <param name="from">
    /// The vertical offset the number starts at.
    /// </param>
    /// <param name="to">
    /// The vertical offset the number ends at.
    /// </param>
    /// <param name="fadeIn">
    /// Whether the number fades in (the new price) rather than out (the old one).
    /// </param>
    /// <returns>
    /// The animation.
    /// </returns>
    private static Animation Roll(double from, double to, bool fadeIn) => new()
    {
        Duration = RollDuration,
        Easing = new CubicEaseOut(),
        FillMode = FillMode.Forward,
        Children =
        {
            new KeyFrame
            {
                Cue = new Cue(0),
                Setters = { new Setter(TranslateTransform.YProperty, from), new Setter(Visual.OpacityProperty, fadeIn ? 0.0 : 1.0) },
            },
            new KeyFrame
            {
                Cue = new Cue(1),
                Setters = { new Setter(TranslateTransform.YProperty, to), new Setter(Visual.OpacityProperty, fadeIn ? 1.0 : 0.0) },
            },
        },
    };

    /// <summary>
    /// Puts the two numbers of the roll back to rest: the price in its place, the old price out of sight.
    /// </summary>
    /// <param name="current">
    /// The new price.
    /// </param>
    /// <param name="old">
    /// The old price.
    /// </param>
    private static void ResetPrice(TextBlock current, TextBlock old)
    {
        current.Opacity = 1;
        SetOffset(current, 0);
        old.Opacity = 0;
        SetOffset(old, 0);
        old.IsVisible = false;
    }

    /// <summary>
    /// Moves a number up or down by setting the translate transform it has.
    /// </summary>
    /// <param name="text">
    /// The number.
    /// </param>
    /// <param name="y">
    /// The vertical offset.
    /// </param>
    private static void SetOffset(TextBlock text, double y)
    {
        if (text.RenderTransform is TranslateTransform translate)
        {
            translate.Y = y;
        }
    }

    /// <summary>
    /// Switches the observed view model.
    /// </summary>
    /// <param name="next">
    /// The view model to observe, or <see langword="null"/> to stop observing.
    /// </param>
    private void Observe(AssetRowViewModel? next)
    {
        if (ReferenceEquals(_observed, next))
        {
            return;
        }

        if (_observed is not null)
        {
            _observed.PropertyChanged -= OnViewModelPropertyChanged;
        }

        StopPrice();
        StopAlert();
        _observed = next;
        if (_observed is not null)
        {
            _observed.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    /// <summary>
    /// Plays a flash when the view model announces a new tick or an alert.
    /// </summary>
    /// <param name="sender">
    /// The view model.
    /// </param>
    /// <param name="e">
    /// The property change data.
    /// </param>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_observed is null)
        {
            return;
        }

        if (e.PropertyName == nameof(AssetRowViewModel.RollEnabled) && !_observed.RollEnabled)
        {
            StopPrice();
            return;
        }

        // A view that is hidden, for example the list while the cards are shown, has nobody to see the flash.
        if (e.PropertyName == nameof(AssetRowViewModel.AlertFlashVersion))
        {
            if (RowAnimationPolicy.ShouldPlayAlert(_view.IsEffectivelyVisible))
            {
                StopAlert();
                StartAlert(_observed.AlertFlashUp);
            }
        }
        else if (e.PropertyName == nameof(AssetRowViewModel.RollVersion)
            && RowAnimationPolicy.ShouldPlay(_observed.RollEnabled, _view.IsEffectivelyVisible, _playingPrice is not null))
        {
            // One that is still playing is not restarted, which caps the work per row however fast the prices arrive.
            StartPrice(_observed.RollUp);
        }
    }

    /// <summary>
    /// Rolls the price: the old number slides out and the new one slides in.
    /// </summary>
    /// <param name="up">
    /// Whether the price rose.
    /// </param>
    private void StartPrice(bool up)
    {
        if (_view.FindControl<TextBlock>("PriceText") is not { } current
            || _view.FindControl<TextBlock>("PriceOld") is not { } old
            || current.Bounds.Height <= 0)
        {
            return;
        }

        var (oldTo, newFrom) = PriceRollMath.Offsets(up, current.Bounds.Height);

        // The start is set here and not left to the first frame of the animation, so that the new price is not seen at its
        // place for a moment before it rolls in.
        old.IsVisible = true;
        old.Opacity = 1;
        SetOffset(old, 0);
        current.Opacity = 0;
        SetOffset(current, newFrom);

        var source = new CancellationTokenSource();
        _playingPrice = source;
        _ = PlayRollAsync(current, old, Roll(newFrom, 0, fadeIn: true), Roll(0, oldTo, fadeIn: false), source);
    }

    /// <summary>
    /// Runs the two animations of the roll until they end or are stopped.
    /// </summary>
    /// <param name="current">
    /// The new price.
    /// </param>
    /// <param name="old">
    /// The old price.
    /// </param>
    /// <param name="slideIn">
    /// The animation of the new price.
    /// </param>
    /// <param name="slideOut">
    /// The animation of the old price.
    /// </param>
    /// <param name="playing">
    /// Cancels the animations; also marks them as the ones that are playing.
    /// </param>
    /// <returns>
    /// A task that completes when the roll is over.
    /// </returns>
    private async Task PlayRollAsync(TextBlock current, TextBlock old, Animation slideIn, Animation slideOut, CancellationTokenSource playing)
    {
        try
        {
            await Task.WhenAll(slideIn.RunAsync(current, playing.Token), slideOut.RunAsync(old, playing.Token));
        }
        catch (OperationCanceledException)
        {
            // Stopped on purpose; whoever stopped it has put the numbers back.
            return;
        }
        finally
        {
            if (ReferenceEquals(_playingPrice, playing))
            {
                _playingPrice = null;
                ResetPrice(current, old);
            }

            playing.Dispose();
        }
    }

    /// <summary>
    /// Tints the whole row or card in the color of the direction and pulses it.
    /// </summary>
    /// <param name="up">
    /// Whether the alert is the one for a price that rises.
    /// </param>
    private void StartAlert(bool up)
    {
        if (_view.FindControl<Border>("Flash") is not { } overlay)
        {
            return;
        }

        var key = up ? "UpBrush" : "DownBrush";
        if (Application.Current?.TryGetResource(key, _view.ActualThemeVariant, out var brush) == true && brush is IBrush typed)
        {
            overlay.Background = typed;
            var source = new CancellationTokenSource();
            _playingAlert = source;
            _ = PlayAsync(overlay, AlertAnimation, source);
        }
    }

    /// <summary>
    /// Runs the animation of the flash of an alert until it ends or is stopped.
    /// </summary>
    /// <param name="target">
    /// The control to animate.
    /// </param>
    /// <param name="animation">
    /// The animation to run.
    /// </param>
    /// <param name="playing">
    /// Cancels the animation; also marks it as the one that is playing.
    /// </param>
    /// <returns>
    /// A task that completes when the animation is over.
    /// </returns>
    private async Task PlayAsync(Control target, Animation animation, CancellationTokenSource playing)
    {
        try
        {
            await animation.RunAsync(target, playing.Token);
        }
        catch (OperationCanceledException)
        {
            // Stopped on purpose.
        }
        finally
        {
            if (ReferenceEquals(_playingAlert, playing))
            {
                _playingAlert = null;
            }

            playing.Dispose();
        }
    }

    /// <summary>
    /// Stops the roll of the price, so the numbers are back at rest right away.
    /// </summary>
    private void StopPrice()
    {
        var source = _playingPrice;
        _playingPrice = null;
        if (source is null)
        {
            return;
        }

        source.Cancel();

        // Not left to the animation: wherever the numbers had got to, they must be at rest once the roll is stopped.
        if (_view.FindControl<TextBlock>("PriceText") is { } current && _view.FindControl<TextBlock>("PriceOld") is { } old)
        {
            ResetPrice(current, old);
        }
    }

    /// <summary>
    /// Stops the flash of an alert, so the tint is gone right away.
    /// </summary>
    private void StopAlert()
    {
        var source = _playingAlert;
        _playingAlert = null;
        if (source is null)
        {
            return;
        }

        source.Cancel();

        // Not left to the animation: whatever tint it had reached must be gone once the flash is stopped.
        if (_view.FindControl<Border>("Flash") is { } overlay)
        {
            overlay.Opacity = 0;
        }
    }
}

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
/// The rule that decides whether a price change starts a flash.
/// </summary>
internal static class FlashPolicy
{
    /// <summary>
    /// Tells whether a new flash should start.
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
}

/// <summary>
/// Plays a short tint in the direction of the last move on the control named <c>Flash</c> of a view whenever its
/// row view model announces a price change. It only listens while the view is part of the visual tree, so a view that
/// is taken out of the tree does not stay reachable from a long-lived view model. A view that cannot be seen plays
/// nothing, a flash that is playing is not restarted by the next tick, and turning the flash off stops it at once.
/// </summary>
internal sealed class FlashAnimator
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
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0.22) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 0.0) } },
        },
    };

    /// <summary>
    /// The view that hosts the flash overlay.
    /// </summary>
    private readonly UserControl _view;

    /// <summary>
    /// The view model whose changes are being observed.
    /// </summary>
    private AssetRowViewModel? _observed;

    /// <summary>
    /// Cancels the flash that is playing, or <see langword="null"/> when none is.
    /// </summary>
    private CancellationTokenSource? _playing;

    /// <summary>
    /// Initializes a new instance of the <see cref="FlashAnimator"/> class and wires it to the view.
    /// </summary>
    /// <param name="view">
    /// The view to animate. Its data context is expected to be an <see cref="AssetRowViewModel"/>.
    /// </param>
    public FlashAnimator(UserControl view)
    {
        _view = view;
        view.DataContextChanged += (_, _) => Observe(view.IsAttachedToVisualTree() ? view.DataContext as AssetRowViewModel : null);
        view.AttachedToVisualTree += (_, _) => Observe(view.DataContext as AssetRowViewModel);
        view.DetachedFromVisualTree += (_, _) => Observe(null);
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

        Stop();
        _observed = next;
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
        if (_observed is null)
        {
            return;
        }

        if (e.PropertyName == nameof(AssetRowViewModel.FlashEnabled) && !_observed.FlashEnabled)
        {
            Stop();
            return;
        }

        // A view that is hidden, for example the list while the cards are shown, has nobody to see the flash. One
        // that is still playing is not restarted, which caps the work per row however fast the prices arrive.
        if (e.PropertyName != nameof(AssetRowViewModel.FlashVersion)
            || !FlashPolicy.ShouldPlay(_observed.FlashEnabled, _view.IsEffectivelyVisible, _playing is not null))
        {
            return;
        }

        var flash = _view.FindControl<Border>("Flash");
        if (flash is null)
        {
            return;
        }

        var key = _observed.FlashUp ? "UpBrush" : "DownBrush";
        if (Application.Current?.TryGetResource(key, _view.ActualThemeVariant, out var brush) == true && brush is IBrush typed)
        {
            flash.Background = typed;
            var playing = new CancellationTokenSource();
            _playing = playing;
            _ = PlayAsync(flash, playing);
        }
    }

    /// <summary>
    /// Runs the flash animation until it ends or is stopped.
    /// </summary>
    /// <param name="flash">
    /// The overlay to animate.
    /// </param>
    /// <param name="playing">
    /// Cancels the animation; also marks it as the one that is playing.
    /// </param>
    /// <returns>
    /// A task that completes when the animation is over.
    /// </returns>
    private async Task PlayAsync(Border flash, CancellationTokenSource playing)
    {
        try
        {
            await FlashAnimation.RunAsync(flash, playing.Token);
        }
        catch (OperationCanceledException)
        {
            // Stopped on purpose.
        }
        finally
        {
            if (ReferenceEquals(_playing, playing))
            {
                _playing = null;
            }

            playing.Dispose();
        }
    }

    /// <summary>
    /// Stops the flash that is playing, so the overlay is clear again right away.
    /// </summary>
    private void Stop()
    {
        var playing = _playing;
        _playing = null;
        if (playing is null)
        {
            return;
        }

        playing.Cancel();

        // Not left to the animation: whatever tint it had reached must be gone once the flash is stopped.
        if (_view.FindControl<Border>("Flash") is { } flash)
        {
            flash.Opacity = 0;
        }
    }
}

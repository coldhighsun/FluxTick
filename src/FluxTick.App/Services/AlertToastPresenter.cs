using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using FluxTick.App.ViewModels;
using FluxTick.App.Views;
using FluxTick.Core.Application;

namespace FluxTick.App.Services;

/// <summary>
/// Shows the alerts that fired as small windows at the bottom right of the screen. Use it on the UI thread only.
/// </summary>
public sealed class AlertToastPresenter
{
    /// <summary>
    /// How many notifications are shown at once; the oldest one makes room for a new one.
    /// </summary>
    private const int MaxShown = 4;

    /// <summary>
    /// How long a notification stays.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Brings the main window forward.
    /// </summary>
    private readonly Action _openMainWindow;

    /// <summary>
    /// The notifications that are shown, the newest first.
    /// </summary>
    private readonly List<AlertToastWindow> _shown = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="AlertToastPresenter"/> class.
    /// </summary>
    /// <param name="openMainWindow">
    /// Brings the main window forward, for a click on a notification.
    /// </param>
    public AlertToastPresenter(Action openMainWindow) => _openMainWindow = openMainWindow;

    /// <summary>
    /// Shows the notification of an alert that fired.
    /// </summary>
    /// <param name="alert">
    /// The alert.
    /// </param>
    public void Show(AlertTriggeredEventArgs alert) => Show(AlertText.Title(alert), AlertText.Body(alert));

    /// <summary>
    /// Shows a notification with a title and a text.
    /// </summary>
    /// <param name="title">
    /// The title.
    /// </param>
    /// <param name="body">
    /// The text below the title.
    /// </param>
    public void Show(string title, string body)
    {
        var window = new AlertToastWindow { DataContext = new AlertToastViewModel(title, body) };
        window.Clicked += (_, _) =>
        {
            Close(window);
            _openMainWindow();
        };

        var timer = new DispatcherTimer { Interval = Lifetime };
        timer.Tick += (_, _) => Close(window);
        window.Closed += (_, _) =>
        {
            timer.Stop();
            _shown.Remove(window);
            Layout();
        };

        _shown.Insert(0, window);
        while (_shown.Count > MaxShown)
        {
            _shown[^1].Close();
        }

        window.Show();
        Layout();
        timer.Start();
    }

    /// <summary>
    /// Closes a notification, if it is still shown.
    /// </summary>
    /// <param name="window">
    /// The notification.
    /// </param>
    private void Close(AlertToastWindow window)
    {
        if (_shown.Contains(window))
        {
            window.Close();
        }
    }

    /// <summary>
    /// Puts the notifications in a column in the work area of the primary screen.
    /// </summary>
    private void Layout()
    {
        if (_shown.Count == 0)
        {
            return;
        }

        var screen = _shown[0].Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var sizes = _shown.Select(w =>
        {
            w.Measure(new Size(w.Width, double.PositiveInfinity));
            return PixelSize.FromSize(w.DesiredSize, screen.Scaling);
        }).ToArray();
        var positions = ToastStackMath.Place(screen.WorkingArea, sizes);
        for (var i = 0; i < _shown.Count; i++)
        {
            _shown[i].Position = positions[i];
        }
    }
}

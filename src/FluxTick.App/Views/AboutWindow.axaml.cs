using System.Diagnostics;
using Avalonia.Interactivity;
using FluxTick.App.ViewModels;

namespace FluxTick.App.Views;

/// <summary>
/// The window that shows the version, author, license and project home page of the app.
/// </summary>
public sealed partial class AboutWindow : ToolWindow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AboutWindow"/> class.
    /// </summary>
    public AboutWindow()
    {
        InitializeComponent();

        // The window is as tall as its content, so there is no empty space below it.
        MinHeight = 0;
        Height = double.NaN;
        SizeToContent = Avalonia.Controls.SizeToContent.Height;
        CanResize = false;
    }

    /// <summary>
    /// Closes the window.
    /// </summary>
    /// <param name="sender">
    /// The button.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Opens the project home page in the default browser.
    /// </summary>
    /// <param name="sender">
    /// The button.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnHomepageClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AboutViewModel about)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(about.HomepageUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No browser is registered; there is nothing the window can do about it.
        }
    }
}

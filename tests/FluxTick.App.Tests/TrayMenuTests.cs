using System.Collections.Concurrent;
using FluxTick.App.Localization;
using FluxTick.App.Services;
using FluxTick.App.ViewModels;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for what the tray menu and the close button rely on.
/// </summary>
public class TrayMenuTests
{
    /// <summary>
    /// Verifies a request for a tool window is ignored while another is open, which is why choosing one from the tray menu
    /// must close the open one first, and honored once it is gone.
    /// </summary>
    [Fact]
    public void ShowAbout_IsIgnoredWhileAToolWindowIsOpen()
    {
        var harness = new Harness();
        var requests = 0;
        harness.Main.AboutRequested += (_, _) => requests++;

        harness.Main.BeginToolWindow(ToolWindowKind.Settings);
        harness.Main.ShowAbout();
        requests.Should().Be(0);

        harness.Main.EndToolWindow();
        harness.Main.ShowAbout();
        requests.Should().Be(1);
    }

    /// <summary>
    /// Verifies the close button says what it does under the setting, and follows the setting.
    /// </summary>
    [Fact]
    public void CloseButtonTip_FollowsTheSetting()
    {
        var harness = new Harness();
        var changes = new ConcurrentQueue<string?>();
        harness.SettingsPage.PropertyChanged += (_, e) => changes.Enqueue(e.PropertyName);

        harness.SettingsPage.CloseToTray = true;
        harness.SettingsPage.CloseButtonTip.Should().Be(Loc.Get("Main_HideToTray"));

        harness.SettingsPage.CloseToTray = false;
        harness.SettingsPage.CloseButtonTip.Should().Be(Loc.Get("Common_Close"));
        changes.Should().Contain(nameof(SettingsViewModel.CloseButtonTip));
    }

    /// <summary>
    /// Verifies the tip says 'close' once the desktop is found to have no tray, even though the setting is on, and that the
    /// answer is assumed to be yes until it arrives. The probe is held back, so the order does not depend on timing.
    /// </summary>
    [Fact]
    public async Task CloseButtonTip_WithoutATray_SaysClose()
    {
        using var answer = new ManualResetEventSlim();
        var harness = new Harness(trayProbe: () =>
        {
            answer.Wait();
            return false;
        });
        try
        {
            harness.SettingsPage.CloseToTray = true;
            var changed = new TaskCompletionSource();
            harness.SettingsPage.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SettingsViewModel.CloseButtonTip))
                {
                    changed.TrySetResult();
                }
            };

            harness.SettingsPage.CloseButtonTip.Should().Be(Loc.Get("Main_HideToTray"), "the answer is assumed to be yes until it arrives");

            answer.Set();
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));

            harness.SettingsPage.CloseButtonTip.Should().Be(Loc.Get("Common_Close"));
        }
        finally
        {
            answer.Set();
        }
    }

    /// <summary>
    /// Verifies the hover setting is offered on Windows and hidden once the watcher could not be started.
    /// </summary>
    [Fact]
    public void TrayHoverAvailability_FollowsTheWatcher()
    {
        if (!OperatingSystem.IsWindows())
        {
            TrayHoverAvailability.IsSupported.Should().BeFalse();
            return;
        }

        try
        {
            TrayHoverAvailability.ReportSuccess();
            TrayHoverAvailability.IsSupported.Should().BeTrue();

            TrayHoverAvailability.ReportFailure();
            TrayHoverAvailability.IsSupported.Should().BeFalse();
        }
        finally
        {
            TrayHoverAvailability.ReportSuccess();
        }
    }
}

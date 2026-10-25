using System.Collections.Concurrent;
using FluxTick.Abstractions;
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
    public async Task ShowAbout_IsIgnoredWhileAToolWindowIsOpen()
    {
        await using var harness = new Harness();
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
    public async Task CloseButtonTip_FollowsTheSetting()
    {
        await using var harness = new Harness();
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
        await using var harness = new Harness(trayProbe: () =>
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

    /// <summary>
    /// Verifies the availability announces a change once, and only when the state really changes.
    /// </summary>
    [Fact]
    public void TrayHoverAvailability_AnnouncesOnlyRealChanges()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows can tell where the tray icon is.");

        var changes = 0;
        void OnChanged(object? sender, EventArgs e) => Interlocked.Increment(ref changes);

        TrayHoverAvailability.ReportSuccess();
        TrayHoverAvailability.Changed += OnChanged;
        try
        {
            TrayHoverAvailability.ReportSuccess();
            changes.Should().Be(0);

            TrayHoverAvailability.ReportFailure();
            TrayHoverAvailability.ReportFailure();
            changes.Should().Be(1);

            TrayHoverAvailability.ReportSuccess();
            changes.Should().Be(2);
        }
        finally
        {
            TrayHoverAvailability.Changed -= OnChanged;
            TrayHoverAvailability.ReportSuccess();
        }
    }

    /// <summary>
    /// Verifies a listener that throws neither reaches the caller nor keeps the other listeners from hearing of the change.
    /// </summary>
    [Fact]
    public void TrayHoverAvailability_ThrowingListener_DoesNotStopTheOthers()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows can tell where the tray icon is.");

        var heard = 0;
        var reported = new ConcurrentQueue<Exception>();
        void Throws(object? sender, EventArgs e) => throw new InvalidOperationException("broken listener");
        void Hears(object? sender, EventArgs e) => Interlocked.Increment(ref heard);

        TrayHoverAvailability.ReportSuccess();
        var before = TrayHoverAvailability.ListenerFailed;
        TrayHoverAvailability.ListenerFailed = reported.Enqueue;
        TrayHoverAvailability.Changed += Throws;
        TrayHoverAvailability.Changed += Hears;
        try
        {
            TrayHoverAvailability.ReportFailure();

            heard.Should().Be(1);
            reported.Should().ContainSingle().Which.Message.Should().Be("broken listener");
        }
        finally
        {
            TrayHoverAvailability.Changed -= Throws;
            TrayHoverAvailability.Changed -= Hears;
            TrayHoverAvailability.ListenerFailed = before;
            TrayHoverAvailability.ReportSuccess();
        }
    }

    /// <summary>
    /// Verifies a hook that reports failures and throws itself does not break the announcement.
    /// </summary>
    [Fact]
    public void TrayHoverAvailability_ThrowingHook_DoesNotBreakTheAnnouncement()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows can tell where the tray icon is.");

        var heard = 0;
        void Throws(object? sender, EventArgs e) => throw new InvalidOperationException("broken listener");
        void Hears(object? sender, EventArgs e) => Interlocked.Increment(ref heard);

        TrayHoverAvailability.ReportSuccess();
        var before = TrayHoverAvailability.ListenerFailed;
        TrayHoverAvailability.ListenerFailed = _ => throw new InvalidOperationException("broken hook");
        TrayHoverAvailability.Changed += Throws;
        TrayHoverAvailability.Changed += Hears;
        try
        {
            var announce = TrayHoverAvailability.ReportFailure;

            announce.Should().NotThrow();
            heard.Should().Be(1);
        }
        finally
        {
            TrayHoverAvailability.Changed -= Throws;
            TrayHoverAvailability.Changed -= Hears;
            TrayHoverAvailability.ListenerFailed = before;
            TrayHoverAvailability.ReportSuccess();
        }
    }

    /// <summary>
    /// Verifies the main window and the settings page follow the availability, so the menus and the tray tab appear and
    /// disappear without being rebuilt.
    /// </summary>
    [Fact]
    public async Task ViewModels_FollowTheAvailability()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows can tell where the tray icon is.");

        TrayHoverAvailability.ReportSuccess();
        await using var harness = new Harness(Harness.WatchlistOf(new Asset(Harness.Crypto, "BTC", "Bitcoin", AssetClass.Crypto)));
        await harness.Main.InitializeAsync();
        var changes = new ConcurrentQueue<string?>();
        harness.Main.Rows.Single().PropertyChanged += (_, e) => changes.Enqueue(e.PropertyName);
        harness.SettingsPage.PropertyChanged += (_, e) => changes.Enqueue(e.PropertyName);
        harness.Main.Rows.Single().CanAddToTray.Should().BeTrue();
        try
        {
            TrayHoverAvailability.ReportFailure();

            changes.Should().Contain(nameof(AssetRowViewModel.CanAddToTray));
            changes.Should().Contain(nameof(SettingsViewModel.IsTrayHoverSupported));
            harness.SettingsPage.IsTrayHoverSupported.Should().BeFalse();
            harness.Main.Rows.Should().OnlyContain(r => !r.CanAddToTray && !r.CanRemoveFromTray);
        }
        finally
        {
            TrayHoverAvailability.ReportSuccess();
        }
    }

    /// <summary>
    /// Verifies a view model that was disposed stops following the availability.
    /// </summary>
    [Fact]
    public async Task ViewModels_AfterDispose_StopFollowingTheAvailability()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows can tell where the tray icon is.");

        TrayHoverAvailability.ReportSuccess();
        var harness = new Harness(Harness.WatchlistOf(new Asset(Harness.Crypto, "BTC", "Bitcoin", AssetClass.Crypto)));
        await harness.Main.InitializeAsync();
        var changes = new ConcurrentQueue<string?>();
        harness.Main.Rows.Single().PropertyChanged += (_, e) => changes.Enqueue(e.PropertyName);
        harness.SettingsPage.PropertyChanged += (_, e) => changes.Enqueue(e.PropertyName);
        await harness.DisposeAsync();
        try
        {
            TrayHoverAvailability.ReportFailure();

            changes.Should().BeEmpty();
        }
        finally
        {
            TrayHoverAvailability.ReportSuccess();
        }
    }
}

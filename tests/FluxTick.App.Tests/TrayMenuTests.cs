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
    /// Verifies the menus and the tray settings are offered exactly where the tray list is.
    /// </summary>
    [Fact]
    public async Task ViewModels_OfferTheTrayListWhereItIsSupported()
    {
        await using var harness = new Harness(Harness.WatchlistOf(new Asset(Harness.Crypto, "BTC", "Bitcoin", AssetClass.Crypto)));
        await harness.Main.InitializeAsync();
        var row = harness.Main.Rows.Single();

        harness.SettingsPage.IsTrayListSupported.Should().Be(TrayPopupAvailability.IsSupported);
        row.CanAddToTray.Should().Be(TrayPopupAvailability.IsSupported);
        row.CanRemoveFromTray.Should().BeFalse();
    }
}

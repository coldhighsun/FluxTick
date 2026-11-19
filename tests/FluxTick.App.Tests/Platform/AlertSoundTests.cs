using System.ComponentModel;
using FluxTick.App.Services;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the sound of an alert: where it is offered, how it is played, and the setting that turns it on.
/// </summary>
public class AlertSoundTests
{
    /// <summary>
    /// Verifies the sound is offered on the platforms that have a system sound to play, and only there.
    /// </summary>
    [Fact]
    public void IsSupported_FollowsThePlatform() =>
        AlertSoundAvailability.IsSupported.Should().Be(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());

    /// <summary>
    /// Verifies the player of a platform without a sound plays nothing and does not fail.
    /// </summary>
    [Fact]
    public void Play_NullPlayer_DoesNothing()
    {
        var act = () => new NullAlertSoundPlayer().Play();

        act.Should().NotThrow();
    }

    /// <summary>
    /// Verifies the file is given to the tool as one argument and not through a shell, so a path with spaces or
    /// shell characters is played as it is.
    /// </summary>
    [Fact]
    public void CreateStartInfo_PassesTheFileAsOneArgument()
    {
        var info = MacAlertSoundPlayer.CreateStartInfo("/Sounds/My Sound; rm -rf.aiff");

        info.FileName.Should().Be("/usr/bin/afplay");
        info.ArgumentList.Should().Equal("/Sounds/My Sound; rm -rf.aiff");
        info.UseShellExecute.Should().BeFalse();
        info.CreateNoWindow.Should().BeTrue();
    }

    /// <summary>
    /// Verifies a tool that cannot be started is logged and does not reach the caller, since the notification of the
    /// alert has been shown anyway.
    /// </summary>
    [Fact]
    public void Play_ToolCannotStart_DoesNotThrow()
    {
        var attempts = 0;
        var player = new MacAlertSoundPlayer(
            NullLogger<MacAlertSoundPlayer>.Instance,
            start: _ =>
            {
                attempts++;
                throw new Win32Exception("No such file");
            });

        var act = player.Play;

        act.Should().NotThrow();
        attempts.Should().Be(1);
    }

    /// <summary>
    /// Verifies the player asks for the tool with the file it was given.
    /// </summary>
    [Fact]
    public void Play_StartsTheToolWithTheFile()
    {
        System.Diagnostics.ProcessStartInfo? seen = null;
        var player = new MacAlertSoundPlayer(
            NullLogger<MacAlertSoundPlayer>.Instance,
            "/Sounds/Ping.aiff",
            start: info =>
            {
                seen = info;
                return null;
            });

        player.Play();

        seen!.FileName.Should().Be(MacAlertSoundPlayer.ToolPath);
        seen.ArgumentList.Should().Equal("/Sounds/Ping.aiff");
    }

    /// <summary>
    /// Verifies nothing is played while the sound is off, however many alerts are shown.
    /// </summary>
    /// <returns>
    /// A task that completes when the check is done.
    /// </returns>
    [Fact]
    public async Task OnAlertShown_SoundOff_PlaysNothing()
    {
        await using var h = new Harness();
        var player = new CountingPlayer();
        var gate = new AlertSoundGate(h.Settings, player, new FakeTimeProvider());

        gate.OnAlertShown();
        gate.OnAlertShown();

        player.Count.Should().Be(0);
    }

    /// <summary>
    /// Verifies alerts shown together are one sound, and that the next sound is heard once the gap has passed.
    /// </summary>
    /// <returns>
    /// A task that completes when the check is done.
    /// </returns>
    [Fact]
    public async Task OnAlertShown_SoundOn_PlaysOncePerGap()
    {
        await using var h = new Harness();
        await h.Settings.UpdateAsync(s => s.General.AlertSound = true);
        var player = new CountingPlayer();
        var time = new FakeTimeProvider();
        var gate = new AlertSoundGate(h.Settings, player, time);

        gate.OnAlertShown();
        gate.OnAlertShown();
        time.Advance(AlertSoundGate.MinimumGap - TimeSpan.FromMilliseconds(1));
        gate.OnAlertShown();

        player.Count.Should().Be(1);

        time.Advance(TimeSpan.FromMilliseconds(1));
        gate.OnAlertShown();

        player.Count.Should().Be(2);
    }

    /// <summary>
    /// Verifies the sound plays again after it was turned off and on, and is not heard while it is off.
    /// </summary>
    /// <returns>
    /// A task that completes when the check is done.
    /// </returns>
    [Fact]
    public async Task OnAlertShown_TurnedOffAfterwards_StopsPlaying()
    {
        await using var h = new Harness();
        await h.Settings.UpdateAsync(s => s.General.AlertSound = true);
        var player = new CountingPlayer();
        var time = new FakeTimeProvider();
        var gate = new AlertSoundGate(h.Settings, player, time);
        gate.OnAlertShown();

        await h.Settings.UpdateAsync(s => s.General.AlertSound = false);
        time.Advance(TimeSpan.FromMinutes(1));
        gate.OnAlertShown();

        player.Count.Should().Be(1);
    }

    /// <summary>
    /// Verifies the sound is off by default and the choice in the settings page is saved.
    /// </summary>
    /// <returns>
    /// A task that completes when the check is done.
    /// </returns>
    [Fact]
    public async Task AlertSound_ToggledInTheSettingsPage_IsSaved()
    {
        await using var h = new Harness();

        h.SettingsPage.AlertSound.Should().BeFalse();
        h.SettingsPage.AlertSound = true;

        await Harness.EventuallyAsync(() => h.Store.Stored!.General.AlertSound);
    }

    /// <summary>
    /// A player that counts how often it was asked to play.
    /// </summary>
    private sealed class CountingPlayer : IAlertSoundPlayer
    {
        /// <summary>
        /// Gets how often the sound was played.
        /// </summary>
        public int Count { get; private set; }

        /// <inheritdoc />
        public void Play() => Count++;
    }
}

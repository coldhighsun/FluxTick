using FluxTick.Core.Application;

namespace FluxTick.App.Services;

/// <summary>
/// Decides whether the notification of an alert is heard: only when the user turned the sound on, and not more than once
/// in a short time, so that alerts that fire together are one sound and not a burst of them. Call it on the UI thread.
/// </summary>
/// <param name="settings">
/// The settings that say whether the sound is on.
/// </param>
/// <param name="player">
/// The player of the sound.
/// </param>
/// <param name="time">
/// The clock, or <see langword="null"/> for the system clock.
/// </param>
public sealed class AlertSoundGate(SettingsManager settings, IAlertSoundPlayer player, TimeProvider? time = null)
{
    /// <summary>
    /// The shortest time between two sounds.
    /// </summary>
    public static readonly TimeSpan MinimumGap = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The clock.
    /// </summary>
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>
    /// The moment the last sound was played, or <see langword="null"/> when none was.
    /// </summary>
    private DateTimeOffset? _lastPlayed;

    /// <summary>
    /// Plays the sound for an alert whose notification is shown now, unless the sound is off or one was played a moment ago.
    /// </summary>
    public void OnAlertShown()
    {
        if (!settings.Current.General.AlertSound)
        {
            return;
        }

        var now = _time.GetUtcNow();
        if (_lastPlayed is { } last && now - last < MinimumGap)
        {
            return;
        }

        _lastPlayed = now;
        player.Play();
    }
}

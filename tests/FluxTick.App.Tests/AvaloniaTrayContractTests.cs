using FluxTick.App.Services;

namespace FluxTick.App.Tests;

/// <summary>
/// Guards the private members of Avalonia that the tray hover list depends on.
/// </summary>
public class AvaloniaTrayContractTests
{
    /// <summary>
    /// Verifies the members the watcher reads are still there, so an upgrade of Avalonia that renames them fails here instead
    /// of silently switching the hover list off.
    /// </summary>
    [Fact]
    public void AvaloniaMembers_AreAvailable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        WindowsTrayHover.AvaloniaMembersAvailable().Should().BeTrue(
            "the tray hover list reads private members of Avalonia; update WindowsTrayHover to the new ones");
    }
}

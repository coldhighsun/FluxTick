using Avalonia.Controls;
using FluxTick.App.Services;
using FluxTick.App.Views;

namespace FluxTick.App.Tests;

public sealed class BossKeyRolesTests
{
    /// <summary>
    /// Verifies that every window class of the app is a tool window or is named as one the boss key brings back or one that
    /// is only there for a moment, so that a new kind of window is decided on here and not left to the fallback.
    /// </summary>
    [Fact]
    public void Of_EveryWindowClassIsDecidedOn()
    {
        var windows = typeof(MainWindow).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(Window).IsAssignableFrom(t) && !typeof(ToolWindow).IsAssignableFrom(t));

        foreach (var window in windows)
        {
            BossKeyRoles.Remembered.Concat(BossKeyRoles.Momentary).Should().Contain(window, $"{window.Name} must be decided on");
        }
    }

    /// <summary>
    /// Verifies the role of each kind of window, and that one nobody decided on is hidden and not brought back.
    /// </summary>
    [Fact]
    public void Of_GivesEachKindItsRole()
    {
        BossKeyRoles.Of(typeof(MainWindow)).Should().Be(BossKeyRole.Remember);
        BossKeyRoles.Of(typeof(AlertToastWindow)).Should().Be(BossKeyRole.Drop);
        BossKeyRoles.Of(typeof(ToolWindow)).Should().Be(BossKeyRole.Close);
        BossKeyRoles.Of(typeof(Window)).Should().Be(BossKeyRole.Drop);
    }
}

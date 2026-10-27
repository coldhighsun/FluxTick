using FluxTick.App.Services;

namespace FluxTick.App.Tests;

public sealed class BossKeyStateTests
{
    /// <summary>
    /// Verifies that only the windows that were visible come back, and that the state is clear afterwards.
    /// </summary>
    [Fact]
    public void Restore_ReturnsOnlyWhatWasHidden()
    {
        var state = new BossKeyState<string>();

        state.Hide(["main", "about"], _ => BossKeyRole.Remember).Hide.Should().Equal("main", "about");
        state.IsHidden.Should().BeTrue();

        state.Restore().Should().Equal("main", "about");
        state.IsHidden.Should().BeFalse();
        state.Restore().Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a window that closed while hidden is not brought back.
    /// </summary>
    [Fact]
    public void Restore_SkipsAWindowThatWasForgotten()
    {
        var state = new BossKeyState<string>();
        state.Hide(["main", "prompt"], _ => BossKeyRole.Remember);

        state.Forget("prompt");

        state.Restore().Should().Equal("main");
    }

    /// <summary>
    /// Verifies that something that shows itself runs at once while the windows are shown, and waits while they are hidden.
    /// </summary>
    [Fact]
    public void RunOrDefer_RunsNowOnlyWhileShown()
    {
        var state = new BossKeyState<string>();
        var ran = new List<string>();

        state.RunOrDefer("a", () => ran.Add("now"));
        state.Hide(["main"], _ => BossKeyRole.Remember);
        state.RunOrDefer("b", () => ran.Add("later"));

        ran.Should().Equal("now");

        state.Restore();
        foreach (var show in state.TakeDeferred())
        {
            show();
        }

        ran.Should().Equal("now", "later");
        state.TakeDeferred().Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a repeating thing held back while hidden is kept once, with its latest state, in the order of its
    /// last request.
    /// </summary>
    [Fact]
    public void RunOrDefer_SameKeyWhileHidden_KeepsTheLatest()
    {
        var state = new BossKeyState<string>();
        var ran = new List<string>();
        state.Hide([], _ => BossKeyRole.Remember);

        state.RunOrDefer("btc", () => ran.Add("btc 1"));
        state.RunOrDefer("eth", () => ran.Add("eth"));
        state.RunOrDefer("btc", () => ran.Add("btc 2"));
        foreach (var show in state.TakeDeferred())
        {
            show();
        }

        ran.Should().Equal("eth", "btc 2");
    }

    /// <summary>
    /// Verifies that each window is sorted by its role, so a window is closed, dropped or remembered and never two of these.
    /// </summary>
    [Fact]
    public void Hide_SortsWindowsByRole()
    {
        var state = new BossKeyState<string>();

        var plan = state.Hide(
            ["main", "settings", "toast", "about"],
            w => w switch
            {
                "settings" => BossKeyRole.Close,
                "toast" => BossKeyRole.Drop,
                _ => BossKeyRole.Remember,
            });

        plan.Close.Should().Equal("settings");
        plan.Drop.Should().Equal("toast");
        plan.Hide.Should().Equal("main", "about");
        state.Restore().Should().Equal("main", "about");
    }
}

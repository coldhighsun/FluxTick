using FluxTick.App.Services;

namespace FluxTick.App.Tests;

public sealed class BossKeyControllerTests
{
    /// <summary>
    /// The calls the controller made, in order.
    /// </summary>
    private readonly List<string> _calls = [];

    /// <summary>
    /// The windows that stay on screen when closed.
    /// </summary>
    private readonly HashSet<string> _refusesToClose = [];

    /// <summary>
    /// The steps that fail.
    /// </summary>
    private readonly HashSet<string> _failing = [];

    /// <summary>
    /// The failures that were reported.
    /// </summary>
    private readonly List<Exception> _errors = [];

    /// <summary>
    /// Verifies that the windows are sorted by role, and that the tray icon goes last on the way out.
    /// </summary>
    [Fact]
    public void Toggle_Hides_ClosesThenDropsThenHidesAndTheTrayIconLast()
    {
        var controller = Create(["main", "settings", "toast"]);

        controller.Toggle();

        _calls.Should().Equal("close settings", "drop toast", "hide main", "tray False");
    }

    /// <summary>
    /// Verifies that the tray icon comes back first, then the windows, then what was held back.
    /// </summary>
    [Fact]
    public void Toggle_Restores_TheTrayIconFirstThenTheWindowsThenWhatWasHeldBack()
    {
        var state = new BossKeyState<string>();
        var controller = Create(["main", "settings"], state);
        controller.Toggle();
        state.RunOrDefer("alert", () => _calls.Add("alert"));
        _calls.Clear();

        controller.Toggle();

        _calls.Should().Equal("tray True", "restore main", "alert");
    }

    /// <summary>
    /// Verifies that a window that stays on screen when closed is hidden.
    /// </summary>
    [Fact]
    public void Toggle_AWindowThatRefusesToClose_IsHidden()
    {
        _refusesToClose.Add("settings");
        var controller = Create(["settings"]);

        controller.Toggle();

        _calls.Should().Equal("close settings", "drop settings", "tray False");
    }

    /// <summary>
    /// Verifies that a window that cannot be shown does not keep the tray icon, the other windows or the held back work
    /// from coming back.
    /// </summary>
    [Fact]
    public void Toggle_AFailingRestore_DoesNotStopTheRest()
    {
        var state = new BossKeyState<string>();
        var controller = Create(["main", "about"], state);
        controller.Toggle();
        state.RunOrDefer("alert", () => _calls.Add("alert"));
        _calls.Clear();
        _failing.Add("restore main");

        controller.Toggle();

        _calls.Should().Equal("tray True", "restore main", "restore about", "alert");
        _errors.Should().ContainSingle();
        state.IsHidden.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a window that cannot be hidden does not keep the tray icon from going.
    /// </summary>
    [Fact]
    public void Toggle_AFailingHide_StillHidesTheTrayIcon()
    {
        _failing.Add("hide main");
        var controller = Create(["main"]);

        controller.Toggle();

        _calls.Should().Equal("hide main", "tray False");
        _errors.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies that windows that cannot be listed skip the key press without hiding anything or ending the app.
    /// </summary>
    [Fact]
    public void Toggle_WindowsThatCannotBeListed_SkipsThePress()
    {
        var state = new BossKeyState<string>();
        var controller = new BossKeyController<string>(
            state,
            new BossKeyActions<string>(
                Visible: () => throw new InvalidOperationException("changed"),
                RoleOf: _ => BossKeyRole.Remember,
                IsVisible: _ => true,
                Close: _ => _calls.Add("close"),
                HideForGood: _ => _calls.Add("drop"),
                HideToRestore: _ => _calls.Add("hide"),
                Restore: _ => _calls.Add("restore"),
                SetTrayVisible: shown => _calls.Add($"tray {shown}"),
                OnError: _errors.Add));

        controller.Toggle();

        _calls.Should().BeEmpty();
        _errors.Should().ContainSingle();
        state.IsHidden.Should().BeFalse();
    }

    /// <summary>
    /// Makes a controller over fake windows: "main" and "about" come back, "settings" is a tool window and "toast" is
    /// momentary.
    /// </summary>
    /// <param name="visible">
    /// The windows that are visible.
    /// </param>
    /// <param name="state">
    /// What the boss key hid, or <see langword="null"/> for a fresh one.
    /// </param>
    /// <returns>
    /// The controller.
    /// </returns>
    private BossKeyController<string> Create(string[] visible, BossKeyState<string>? state = null)
    {
        void Step(string call)
        {
            _calls.Add(call);
            if (_failing.Contains(call))
            {
                throw new InvalidOperationException(call);
            }
        }

        return new BossKeyController<string>(
            state ?? new BossKeyState<string>(),
            new BossKeyActions<string>(
                () => visible,
                w => w switch
                {
                    "settings" => BossKeyRole.Close,
                    "toast" => BossKeyRole.Drop,
                    _ => BossKeyRole.Remember,
                },
                w => _refusesToClose.Contains(w),
                w => Step($"close {w}"),
                w => Step($"drop {w}"),
                w => Step($"hide {w}"),
                w => Step($"restore {w}"),
                shown => Step($"tray {shown}"),
                _errors.Add));
    }
}

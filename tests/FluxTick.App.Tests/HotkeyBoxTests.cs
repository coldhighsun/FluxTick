using FluxTick.App.Localization;
using Avalonia.Input;
using FluxTick.App.Views;

namespace FluxTick.App.Tests;

public sealed class HotkeyBoxTests
{
    /// <summary>
    /// Verifies that the box shows the value it is given, which is what the settings page opens with.
    /// </summary>
    [Fact]
    public void Value_IsShown()
    {
        var box = new HotkeyBox { Value = "Alt+2" };

        box.DisplayText.Should().Be("Alt+2");
    }

    /// <summary>
    /// Verifies that a box without a hotkey shows its empty text rather than nothing, and the value once there is one.
    /// </summary>
    [Fact]
    public void EmptyValue_ShowsTheEmptyText()
    {
        var box = new HotkeyBox { EmptyText = "None" };

        box.DisplayText.Should().Be("None");

        box.Value = "Alt+2";
        box.DisplayText.Should().Be("Alt+2");

        box.Value = string.Empty;
        box.DisplayText.Should().Be("None");
    }

    /// <summary>
    /// Verifies that a focused box without a hotkey asks for keys, that held modifiers win over the hint, and that the
    /// empty text comes back when the focus leaves.
    /// </summary>
    [Fact]
    public void EmptyValue_WhileRecording_ShowsTheRecordingText()
    {
        var box = new HotkeyBox { EmptyText = "None", RecordingText = "Press keys" };

        box.FocusChanged(hasFocus: true);
        box.DisplayText.Should().Be("Press keys");

        Press(box, Key.LeftCtrl, KeyModifiers.Control);
        box.DisplayText.Should().Be("Ctrl+");

        Release(box, Key.LeftCtrl, KeyModifiers.Control);
        box.DisplayText.Should().Be("Press keys");

        box.FocusChanged(hasFocus: false);
        box.DisplayText.Should().Be("None");
    }

    /// <summary>
    /// Verifies that the boss key hint texts exist in both languages.
    /// </summary>
    [Theory]
    [InlineData("Settings_BossKeyNone")]
    [InlineData("Settings_BossKeyPress")]
    public void BossKeyHints_AreTranslated(string key)
    {
        Loc.Instance[key].Should().NotBe(key);
    }

    /// <summary>
    /// Verifies that held modifiers show at once with a trailing plus and go away with the keys when no combination was
    /// completed.
    /// </summary>
    [Fact]
    public void HeldModifiers_ShowAndGoAwayWhenReleased()
    {
        var box = new HotkeyBox { Value = "Alt+2" };

        Press(box, Key.LeftCtrl, KeyModifiers.Control);
        box.DisplayText.Should().Be("Ctrl+");

        Press(box, Key.LeftAlt, KeyModifiers.Control | KeyModifiers.Alt);
        box.DisplayText.Should().Be("Ctrl+Alt+");

        Release(box, Key.LeftAlt, KeyModifiers.Control | KeyModifiers.Alt);
        box.DisplayText.Should().Be("Ctrl+");

        Release(box, Key.LeftCtrl, KeyModifiers.Control);
        box.DisplayText.Should().Be("Alt+2");
        box.Value.Should().Be("Alt+2");
    }

    /// <summary>
    /// Verifies that a completed combination is set, and that letting go of the main key first does not show the modifiers
    /// that are still held as the start of another one.
    /// </summary>
    [Fact]
    public void CompletedCombination_IsSet_AndTheHeldModifiersAreNotShownAgain()
    {
        var box = new HotkeyBox();
        var held = KeyModifiers.Control | KeyModifiers.Alt;

        Press(box, Key.LeftCtrl, KeyModifiers.Control);
        Press(box, Key.LeftAlt, held);
        Press(box, Key.J, held);

        box.Value.Should().Be("Ctrl+Alt+J");
        box.DisplayText.Should().Be("Ctrl+Alt+J");

        Release(box, Key.J, held);
        box.DisplayText.Should().Be("Ctrl+Alt+J");

        Release(box, Key.LeftAlt, held);
        Release(box, Key.LeftCtrl, KeyModifiers.Control);
        box.DisplayText.Should().Be("Ctrl+Alt+J");
    }

    /// <summary>
    /// Verifies that Backspace clears the value and that Escape is left for the window.
    /// </summary>
    [Fact]
    public void Backspace_Clears_EscapeIsLeftAlone()
    {
        var box = new HotkeyBox { Value = "Alt+2" };

        Press(box, Key.Escape, KeyModifiers.None).Handled.Should().BeFalse();
        box.Value.Should().Be("Alt+2");

        Press(box, Key.Back, KeyModifiers.None).Handled.Should().BeTrue();
        box.Value.Should().BeEmpty();
        box.DisplayText.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that the box records while it has the focus, and not once the focus or the window is gone.
    /// </summary>
    [Fact]
    public void IsCapturing_FollowsTheFocusAndTheWindow()
    {
        var box = new HotkeyBox();
        box.IsCapturing.Should().BeFalse();

        box.FocusChanged(hasFocus: true);
        box.IsCapturing.Should().BeTrue();

        box.WindowActivationChanged(active: false);
        box.IsCapturing.Should().BeFalse();

        box.WindowActivationChanged(active: true);
        box.IsCapturing.Should().BeTrue();

        box.FocusChanged(hasFocus: false);
        box.IsCapturing.Should().BeFalse();

        box.WindowActivationChanged(active: true);
        box.IsCapturing.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that what was shown while keys were held goes away when the recording ends.
    /// </summary>
    [Fact]
    public void LeavingTheBox_DropsWhatWasShown()
    {
        var box = new HotkeyBox { Value = "Alt+2" };
        box.FocusChanged(hasFocus: true);
        Press(box, Key.LeftCtrl, KeyModifiers.Control);
        box.DisplayText.Should().Be("Ctrl+");

        box.WindowActivationChanged(active: false);

        box.DisplayText.Should().Be("Alt+2");
    }

    /// <summary>
    /// Raises a key press on the box.
    /// </summary>
    /// <param name="box">
    /// The box.
    /// </param>
    /// <param name="key">
    /// The key.
    /// </param>
    /// <param name="modifiers">
    /// The modifiers held, including the key itself when it is a modifier.
    /// </param>
    /// <returns>
    /// The event, to see whether it was handled.
    /// </returns>
    private static KeyEventArgs Press(HotkeyBox box, Key key, KeyModifiers modifiers) => Raise(box, InputElement.KeyDownEvent, key, modifiers);

    /// <summary>
    /// Raises a key release on the box.
    /// </summary>
    /// <param name="box">
    /// The box.
    /// </param>
    /// <param name="key">
    /// The key.
    /// </param>
    /// <param name="modifiers">
    /// The modifiers held, which may still include the key that is released.
    /// </param>
    private static void Release(HotkeyBox box, Key key, KeyModifiers modifiers) => Raise(box, InputElement.KeyUpEvent, key, modifiers);

    /// <summary>
    /// Raises a key event on the box.
    /// </summary>
    /// <param name="box">
    /// The box.
    /// </param>
    /// <param name="routedEvent">
    /// The event.
    /// </param>
    /// <param name="key">
    /// The key.
    /// </param>
    /// <param name="modifiers">
    /// The modifiers held.
    /// </param>
    /// <returns>
    /// The event.
    /// </returns>
    private static KeyEventArgs Raise(HotkeyBox box, Avalonia.Interactivity.RoutedEvent<KeyEventArgs> routedEvent, Key key, KeyModifiers modifiers)
    {
        var e = new KeyEventArgs { RoutedEvent = routedEvent, Key = key, KeyModifiers = modifiers };
        box.RaiseEvent(e);
        return e;
    }
}

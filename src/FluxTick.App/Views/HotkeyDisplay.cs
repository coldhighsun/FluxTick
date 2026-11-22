namespace FluxTick.App.Views;

/// <summary>
/// Spells a hotkey the way the keys are named on the keyboard in front of the user. The settings file keeps the Windows
/// names; on a Mac the Alt key is Option and the Win key is Command.
/// </summary>
internal static class HotkeyDisplay
{
    /// <summary>
    /// Gets or sets a value indicating whether hotkeys are shown with the names of a Mac keyboard. It follows the platform;
    /// tests set it to pin the spelling.
    /// </summary>
    internal static bool UseMacNames { get; set; } = OperatingSystem.IsMacOS();

    /// <summary>
    /// Spells a hotkey, or the modifiers held so far, for the platform.
    /// </summary>
    /// <param name="text">
    /// The text in the settings spelling, for example <c>Ctrl+Alt+H</c> or <c>Ctrl+Alt+</c>.
    /// </param>
    /// <returns>
    /// The text to show.
    /// </returns>
    internal static string Format(string text) => Format(text, UseMacNames);

    /// <summary>
    /// Spells a hotkey, or the modifiers held so far, for a platform.
    /// </summary>
    /// <param name="text">
    /// The text in the settings spelling.
    /// </param>
    /// <param name="mac">
    /// <see langword="true"/> to use the names of a Mac keyboard.
    /// </param>
    /// <returns>
    /// The text to show.
    /// </returns>
    internal static string Format(string text, bool mac)
    {
        if (!mac || text.Length == 0)
        {
            return text;
        }

        var parts = text.Split('+');
        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] = parts[i] switch
            {
                "Alt" => "Option",
                "Win" => "Cmd",
                var other => other,
            };
        }

        return string.Join('+', parts);
    }
}

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;

namespace FluxTick.App.Views;

/// <summary>
/// A small pop-up with a text box that asks for a group name. It is the one place where a name is typed for a new
/// group, wherever the user started from.
/// </summary>
internal static class GroupNamePrompt
{
    /// <summary>
    /// Shows the pop-up next to a control. Enter commits the typed name and Esc, or a click elsewhere, cancels.
    /// </summary>
    /// <param name="anchor">
    /// The control the pop-up opens next to.
    /// </param>
    /// <param name="label">
    /// The text above the text box.
    /// </param>
    /// <param name="placement">
    /// Where the pop-up opens relative to the anchor.
    /// </param>
    /// <param name="commit">
    /// Called with the typed name when the user presses Enter on a name that is not blank.
    /// </param>
    public static void Show(Control anchor, string label, PlacementMode placement, Func<string, Task> commit)
    {
        var box = new TextBox { Width = 200, MaxLength = 40, PlaceholderText = "Group name" };
        var flyout = new Flyout
        {
            Placement = placement,
            Content = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = label, FontSize = 12, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                    box,
                },
            },
        };

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                var name = box.Text;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    flyout.Hide();
                    _ = commit(name);
                }
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                flyout.Hide();
            }
        };

        flyout.Opened += (_, _) => box.Focus();

        // The menu that led here is still closing while the click is handled; opening the pop-up right away would
        // let that closing dismiss it again.
        Dispatcher.UIThread.Post(() => flyout.ShowAt(anchor), DispatcherPriority.Background);
    }
}

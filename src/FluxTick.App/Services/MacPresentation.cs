using static FluxTick.App.Services.ObjC;

namespace FluxTick.App.Services;

/// <summary>
/// Hides the menu bar and the Dock of macOS while a window fills the screen, which is what full screen looks like without
/// the full screen space of the system.
/// </summary>
public static class MacPresentation
{
    /// <summary>
    /// The presentation option that hides the Dock.
    /// </summary>
    private const ulong HideDock = 1 << 1;

    /// <summary>
    /// The presentation option that hides the menu bar; the system wants the Dock hidden with it.
    /// </summary>
    private const ulong HideMenuBar = 1 << 3;

    /// <summary>
    /// Hides or shows the menu bar and the Dock.
    /// </summary>
    /// <param name="hidden">
    /// <see langword="true"/> to hide them, <see langword="false"/> to bring them back.
    /// </param>
    public static void SetBarsHidden(bool hidden)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            var application = Send(GetClass("NSApplication"), Sel("sharedApplication"));
            SendUIntArgument(application, Sel("setPresentationOptions:"), hidden ? HideDock | HideMenuBar : 0);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // The window still fills the screen, just with the bars on top.
        }
    }

    /// <summary>
    /// The window style bit that lets a window be resized, which the system wants before it makes a window full screen.
    /// </summary>
    private const ulong ResizableMask = 1 << 3;

    /// <summary>
    /// The collection behavior bit that lets a window have a full screen space of its own.
    /// </summary>
    private const ulong FullScreenPrimary = 1 << 7;

    /// <summary>
    /// The window style bit the system sets while a window is in its full screen space.
    /// </summary>
    private const ulong FullScreenStyle = 1 << 14;

    /// <summary>
    /// Makes a borderless window ready for the full screen space of the system, which it otherwise enters without being
    /// resized.
    /// </summary>
    /// <param name="window">
    /// The <c>NSWindow</c> of the window.
    /// </param>
    public static void PrepareFullScreen(IntPtr window)
    {
        if (!OperatingSystem.IsMacOS() || window == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var style = SendUInt(window, Sel("styleMask"));
            SendUIntArgument(window, Sel("setStyleMask:"), style | ResizableMask);
            var behavior = SendUInt(window, Sel("collectionBehavior"));
            SendUIntArgument(window, Sel("setCollectionBehavior:"), behavior | FullScreenPrimary);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // The window just keeps the behavior it has.
        }
    }

    /// <summary>
    /// Asks the system to enter or leave the full screen space for a window.
    /// </summary>
    /// <param name="window">
    /// The <c>NSWindow</c> of the window.
    /// </param>
    public static void ToggleFullScreen(IntPtr window)
    {
        if (!OperatingSystem.IsMacOS() || window == IntPtr.Zero)
        {
            return;
        }

        try
        {
            SendPointer(window, Sel("toggleFullScreen:"), IntPtr.Zero);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // The window stays as it is.
        }
    }

    /// <summary>
    /// Determines whether the system has put a window into its full screen space.
    /// </summary>
    /// <param name="window">
    /// The <c>NSWindow</c> of the window.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the window is in the full screen space.
    /// </returns>
    public static bool IsFullScreen(IntPtr window) =>
        OperatingSystem.IsMacOS() && window != IntPtr.Zero && (SendUInt(window, Sel("styleMask")) & FullScreenStyle) != 0;
}

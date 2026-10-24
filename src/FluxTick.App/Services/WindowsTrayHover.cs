using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace FluxTick.App.Services;

/// <summary>
/// Tells when the pointer rests on the tray icon. Avalonia's <see cref="TrayIcon"/> has no hover event, so this reads the
/// window handle and id Avalonia registered the icon with, and twice a second asks the shell where the icon is and where the
/// pointer is. It depends on private members of Avalonia and stays inactive, leaving the plain tooltip, when they are not
/// found.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsTrayHover : IDisposable
{
    /// <summary>
    /// The name of the type behind the tray icon that Avalonia keeps in a private member.
    /// </summary>
    private const string ImplTypeName = "ITrayIconImpl";

    /// <summary>
    /// The name of the Avalonia assembly of the Windows platform.
    /// </summary>
    private const string PlatformAssembly = "Avalonia.Win32";

    /// <summary>
    /// The full name of the Windows platform type, whose handle is the window the tray icons are registered with.
    /// </summary>
    private const string PlatformTypeName = "Avalonia.Win32.Win32Platform";

    /// <summary>
    /// The full name of the Windows tray icon implementation, which keeps the id of the icon.
    /// </summary>
    private const string TrayIconTypeName = "Avalonia.Win32.TrayIconImpl";

    /// <summary>
    /// The name of the field that holds the id of the icon.
    /// </summary>
    private const string IconIdField = "_uniqueId";

    /// <summary>
    /// How often the pointer is looked at.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// How long the choice to keep the icon visible is trusted, in milliseconds, apart from the moment the pointer arrives
    /// on the icon, when it is read again.
    /// </summary>
    private const long PromotedRefreshMs = 5000;

    /// <summary>
    /// The Win32 constant of the low-level mouse hook.
    /// </summary>
    private const int WhMouseLl = 14;

    /// <summary>
    /// The messages of a press of the left, right and middle mouse button.
    /// </summary>
    private static readonly nint[] ButtonDownMessages = [0x0201, 0x0204, 0x0207];

    /// <summary>
    /// The window that owns the tray icon.
    /// </summary>
    private readonly nint _hwnd;

    /// <summary>
    /// The id of the tray icon.
    /// </summary>
    private readonly uint _id;

    /// <summary>
    /// Says whether the pointer is over the popup, which keeps it open.
    /// </summary>
    private readonly Func<bool> _popupHovered;

    /// <summary>
    /// Says whether the popup is shown.
    /// </summary>
    private readonly Func<bool> _popupVisible;

    /// <summary>
    /// Follows whether the icon is kept on the taskbar.
    /// </summary>
    private readonly NotifyIconPromotion _promotion;

    /// <summary>
    /// The timer that looks at the pointer.
    /// </summary>
    private readonly DispatcherTimer _timer;

    /// <summary>
    /// Decides from the samples of the pointer when the popup opens and closes.
    /// </summary>
    private readonly TrayHoverTracker _tracker = new();

    /// <summary>
    /// The hook procedure, kept in a field so the delegate is not collected while the hook is installed.
    /// </summary>
    private readonly HookProc _hookProc;

    /// <summary>
    /// The handle of the mouse hook that is installed while the popup is open, or zero.
    /// </summary>
    private nint _clickHook;

    /// <summary>
    /// The bounds of the icon while the popup is open.
    /// </summary>
    private Rect _openBounds;

    /// <summary>
    /// The window of the flyout that holds the folded-away tray icons, or zero while none was found.
    /// </summary>
    private nint _overflow;

    /// <summary>
    /// When the choice to keep the icon visible was read, in <see cref="Environment.TickCount64"/> milliseconds.
    /// </summary>
    private long _promotedAt = long.MinValue / 2;

    /// <summary>
    /// Whether the registry is being read right now, off the UI thread.
    /// </summary>
    private int _refreshing;

    /// <summary>
    /// Whether the pointer was on the bounds the shell gave at the last look.
    /// </summary>
    private bool _pointerOnBounds;

    /// <summary>
    /// Whether the watcher was disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Whether the shell has told where the icon is at least once.
    /// </summary>
    private bool _located;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsTrayHover"/> class.
    /// </summary>
    /// <param name="hwnd">
    /// The window that owns the tray icon.
    /// </param>
    /// <param name="id">
    /// The id of the tray icon.
    /// </param>
    /// <param name="promotion">
    /// Follows whether the icon is kept on the taskbar.
    /// </param>
    /// <param name="popupHovered">
    /// Says whether the pointer is over the popup.
    /// </param>
    /// <param name="popupVisible">
    /// Says whether the popup is shown.
    /// </param>
    private WindowsTrayHover(nint hwnd, uint id, NotifyIconPromotion promotion, Func<bool> popupHovered, Func<bool> popupVisible)
    {
        _hwnd = hwnd;
        _id = id;
        _promotion = promotion;
        _popupHovered = popupHovered;
        _popupVisible = popupVisible;
        _timer = new DispatcherTimer(Interval, DispatcherPriority.Background, OnTick);
        _hookProc = OnMouseHook;
    }

    /// <summary>
    /// The signature of a hook procedure.
    /// </summary>
    /// <param name="code">
    /// The hook code; a negative one must be passed on untouched.
    /// </param>
    /// <param name="message">
    /// The mouse message.
    /// </param>
    /// <param name="data">
    /// A pointer to the MSLLHOOKSTRUCT, whose first member is the pointer position.
    /// </param>
    /// <returns>
    /// The result of the next hook.
    /// </returns>
    private delegate nint HookProc(int code, nint message, nint data);

    /// <summary>
    /// Raised when the pointer rests on the icon. The argument is the icon bounds in screen pixels.
    /// </summary>
    public event EventHandler<PixelRect>? Entered;

    /// <summary>
    /// Raised when the pointer left the icon and the popup.
    /// </summary>
    public event EventHandler? Left;

    /// <summary>
    /// Raised once the shell first knows where the icon is, which is when hovering starts to work.
    /// </summary>
    public event EventHandler? Located;

    /// <summary>
    /// Starts watching a tray icon.
    /// </summary>
    /// <param name="icon">
    /// The tray icon, which must already be shown.
    /// </param>
    /// <param name="popupHovered">
    /// Says whether the pointer is over the popup.
    /// </param>
    /// <param name="popupVisible">
    /// Says whether the popup is shown.
    /// </param>
    /// <returns>
    /// The watcher, or <see langword="null"/> when the icon's handle could not be found.
    /// </returns>
    public static WindowsTrayHover? TryCreate(TrayIcon icon, Func<bool> popupHovered, Func<bool> popupVisible)
    {
        try
        {
            const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var impl = typeof(TrayIcon).GetProperties(All).Where(p => p.PropertyType.Name == ImplTypeName).Select(p => p.GetValue(icon))
                .Concat(typeof(TrayIcon).GetFields(All).Where(f => f.FieldType.Name == ImplTypeName).Select(f => f.GetValue(icon)))
                .FirstOrDefault(i => i is not null);
            if (impl is null || Environment.ProcessPath is not { } executable)
            {
                return null;
            }

            // Avalonia registers its tray icons with the message window of its Win32 platform.
            var platform = impl.GetType().Assembly.GetType(PlatformTypeName);
            var instance = platform?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
            var hwnd = platform?.GetProperty("Handle", All)?.GetValue(instance);
            var id = impl.GetType().GetField(IconIdField, All)?.GetValue(impl);
            if (hwnd is null || id is null)
            {
                return null;
            }

            var handle = hwnd is IntPtr pointer ? pointer : Convert.ToInt64(hwnd);
            var promotion = new NotifyIconPromotion(
                new RegistryNotifyIconRecords(),
                executable,
                KnownFolder,
                path => NotifyIconPromotion.IsFileGone(path, File.Exists, Directory.Exists));
            var watcher = new WindowsTrayHover((nint)handle, Convert.ToUInt32(id), promotion, popupHovered, popupVisible);

            // Reading and cleaning the records touches files that may be on a drive that is slow or not there, so it stays
            // off the UI thread; until the answer arrives the icon counts as shown, and the dwell time of the tracker covers
            // the gap.
            _ = Task.Run(promotion.DropStale);
            watcher.RefreshPromotedInBackground();
            return watcher;
        }
        catch (Exception ex) when (ex is TargetInvocationException or InvalidCastException or FormatException or OverflowException or MemberAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Tells whether the members of Avalonia that <see cref="TryCreate"/> reads exist in the version in use. Avalonia does
    /// not promise them, so a test asks this to find out about an upgrade that breaks the watcher, which would otherwise just
    /// stop working without any message.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when all of them are there.
    /// </returns>
    public static bool AvaloniaMembersAvailable()
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        try
        {
            var holdsImpl = typeof(TrayIcon).GetProperties(All).Any(p => p.PropertyType.Name == ImplTypeName)
                || typeof(TrayIcon).GetFields(All).Any(f => f.FieldType.Name == ImplTypeName);
            var assembly = Assembly.Load(new AssemblyName(PlatformAssembly));
            var platform = assembly.GetType(PlatformTypeName);
            return holdsImpl
                && platform?.GetProperty("Instance", All) is not null
                && platform.GetProperty("Handle", All) is not null
                && assembly.GetType(TrayIconTypeName)?.GetField(IconIdField, All)?.FieldType == typeof(int);
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Stops watching.
    /// </summary>
    public void Dispose()
    {
        // Nothing runs after this: the timer is the only thing that looks at the pointer, and a tick that was already
        // queued is ignored.
        _disposed = true;
        _timer.Stop();
        RemoveClickHook();
    }

    /// <summary>
    /// Closes the popup at once, for example when the icon is clicked, and keeps it closed until the pointer has left the
    /// icon once.
    /// </summary>
    public void Dismiss()
    {
        RemoveClickHook();
        if (_tracker.Dismiss())
        {
            Left?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Finds the folder of a known folder id.
    /// </summary>
    /// <param name="id">
    /// The id.
    /// </param>
    /// <returns>
    /// The folder, or <see langword="null"/> when the system does not know the id.
    /// </returns>
    private static string? KnownFolder(Guid id)
    {
        if (SHGetKnownFolderPath(ref id, 0, 0, out var buffer) != 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    /// <summary>
    /// Watches the mouse buttons while the popup is open. The popup covers the area where the menu of the icon opens, and a
    /// press is much shorter than the interval between two looks, so a hook sees what polling would miss. It exists only
    /// while the popup is open.
    /// </summary>
    /// <param name="bounds">
    /// The bounds of the icon.
    /// </param>
    private void InstallClickHook(Rect bounds)
    {
        _openBounds = bounds;
        if (_clickHook == 0)
        {
            _clickHook = SetWindowsHookEx(WhMouseLl, _hookProc, GetModuleHandle(null), 0);
        }
    }

    /// <summary>
    /// Removes the mouse hook, if it is installed.
    /// </summary>
    private void RemoveClickHook()
    {
        if (_clickHook != 0)
        {
            UnhookWindowsHookEx(_clickHook);
            _clickHook = 0;
        }
    }

    /// <summary>
    /// Dismisses the popup when a mouse button is pressed on the icon. It must be quick, as every mouse event of the desktop
    /// waits for it, so the dismissal itself is left to the UI thread.
    /// </summary>
    /// <param name="code">
    /// The hook code.
    /// </param>
    /// <param name="message">
    /// The mouse message.
    /// </param>
    /// <param name="data">
    /// A pointer to the MSLLHOOKSTRUCT.
    /// </param>
    /// <returns>
    /// The result of the next hook.
    /// </returns>
    private nint OnMouseHook(int code, nint message, nint data)
    {
        if (code >= 0 && Array.IndexOf(ButtonDownMessages, message) >= 0 && Contains(_openBounds, Marshal.PtrToStructure<Point>(data)))
        {
            Dispatcher.UIThread.Post(Dismiss);
        }

        return CallNextHookEx(0, code, message, data);
    }

    /// <summary>
    /// Looks at the pointer.
    /// </summary>
    /// <param name="sender">
    /// The timer.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnTick(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        var found = TryGetIconRect(out var rect);
        if (found && !_located)
        {
            _located = true;
            Located?.Invoke(this, EventArgs.Empty);
        }

        var onBounds = found && GetCursorPos(out var cursor) && Contains(rect, cursor);
        if (onBounds && !_pointerOnBounds)
        {
            // The record is read again as the pointer arrives, so a choice the user just made is not missed; the dwell time
            // of the tracker leaves room for the answer.
            RefreshPromotedInBackground();
        }

        _pointerOnBounds = onBounds;

        // The overflow is only consulted once the pointer is over what the shell reported.
        var onIcon = onBounds && IsIconShown(rect);
        switch (_tracker.Update(onIcon, _tracker.IsOpen && _popupHovered()))
        {
            case TrayHoverTracker.Step.Open:
                Entered?.Invoke(this, new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top));

                // The popup may have failed to show, for example when no screen was found; then there is nothing to dismiss.
                if (_popupVisible())
                {
                    InstallClickHook(rect);
                }

                break;
            case TrayHoverTracker.Step.Close:
                RemoveClickHook();
                Left?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    /// <summary>
    /// Tells whether the bounds the shell gave are those of the icon itself. A folded-away icon has a position of its own
    /// only while the overflow is open and shows it; otherwise the shell answers with the button that opens the overflow.
    /// </summary>
    /// <param name="rect">
    /// The bounds the shell gave.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the icon is shown there.
    /// </returns>
    private bool IsIconShown(Rect rect)
    {
        if (Environment.TickCount64 - _promotedAt >= PromotedRefreshMs)
        {
            RefreshPromotedInBackground();
        }

        return _promotion.Promoted
            || (TryGetOverflowRect(out var flyout) && Contains(flyout, new Point { X = (rect.Left + rect.Right) / 2, Y = (rect.Top + rect.Bottom) / 2 }));
    }

    /// <summary>
    /// Reads the choice to keep the icon visible on a pool thread, unless that is already going on.
    /// </summary>
    private void RefreshPromotedInBackground()
    {
        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
        {
            return;
        }

        _promotedAt = Environment.TickCount64;
        _ = Task.Run(() =>
        {
            try
            {
                _promotion.Refresh();
            }
            finally
            {
                Volatile.Write(ref _refreshing, 0);
            }
        });
    }

    /// <summary>
    /// Finds the flyout that holds the folded-away tray icons. It is a window of its own that is visible only while the
    /// user has it open.
    /// </summary>
    /// <param name="rect">
    /// The flyout bounds in screen pixels.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the flyout is open.
    /// </returns>
    private bool TryGetOverflowRect(out Rect rect)
    {
        if (_overflow == 0 || !IsWindowVisible(_overflow))
        {
            _overflow = FindWindow("TopLevelWindowForOverflowXamlIsland", null);
            if (_overflow == 0)
            {
                _overflow = FindWindow("NotifyIconOverflowWindow", null);
            }
        }

        rect = default;
        return _overflow != 0 && IsWindowVisible(_overflow) && GetWindowRect(_overflow, out rect);
    }

    /// <summary>
    /// Asks the shell where the tray icon is.
    /// </summary>
    /// <param name="rect">
    /// The icon bounds in screen pixels.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the shell knows the icon.
    /// </returns>
    private bool TryGetIconRect(out Rect rect)
    {
        var id = new NotifyIconIdentifier { Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(), Window = _hwnd, Id = _id };
        return Shell_NotifyIconGetRect(in id, out rect) == 0 && rect.Right > rect.Left;
    }

    /// <summary>
    /// Tells whether a point is inside a rectangle.
    /// </summary>
    /// <param name="rect">
    /// The rectangle.
    /// </param>
    /// <param name="point">
    /// The point.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the point is inside.
    /// </returns>
    private static bool Contains(Rect rect, Point point) => point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom;

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, nint token, out nint path);

    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(in NotifyIconIdentifier identifier, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SetWindowsHookEx(int hook, HookProc procedure, nint module, uint thread);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rect rect);

    /// <summary>
    /// The shell's NOTIFYICONIDENTIFIER.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public Guid Item;
    }

    /// <summary>
    /// The Win32 RECT.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// The Win32 POINT.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}

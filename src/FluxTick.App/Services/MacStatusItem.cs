using System.Runtime.InteropServices;
using static FluxTick.App.Services.ObjC;

namespace FluxTick.App.Services;

/// <summary>
/// The icon of the app in the macOS menu bar. Avalonia does not tell when that icon is clicked unless it has a menu, which
/// opens on every click, so the app makes the status item itself and gets the click from AppKit.
/// </summary>
public sealed class MacStatusItem : IDisposable
{
    /// <summary>
    /// The length that lets AppKit size the item to its icon.
    /// </summary>
    private const double VariableLength = -1;

    /// <summary>
    /// The side of the icon in points, which fits the menu bar.
    /// </summary>
    private const double IconSize = 18;

    /// <summary>
    /// The name of the class that receives the click.
    /// </summary>
    private const string TargetClassName = "FluxTickStatusItemTarget";

    /// <summary>
    /// What to do when the item is clicked; the receiver is created once per process, so this is shared.
    /// </summary>
    private static Action? s_clicked;

    /// <summary>
    /// The click handler of the receiver, held so that the runtime does not collect the function pointer given to AppKit.
    /// </summary>
    private static ClickHandler? s_handler;

    /// <summary>
    /// The receiver of the click.
    /// </summary>
    private static IntPtr s_target;

    /// <summary>
    /// The status item.
    /// </summary>
    private IntPtr _item;

    /// <summary>
    /// The button of the status item.
    /// </summary>
    private IntPtr _button;

    /// <summary>
    /// Initializes a new instance of the <see cref="MacStatusItem"/> class.
    /// </summary>
    /// <param name="item">
    /// The status item.
    /// </param>
    /// <param name="button">
    /// The button of the status item.
    /// </param>
    private MacStatusItem(IntPtr item, IntPtr button)
    {
        _item = item;
        _button = button;
    }

    /// <summary>
    /// The signature AppKit calls for an action: the receiver, the selector and the sender.
    /// </summary>
    /// <param name="self">
    /// The receiver.
    /// </param>
    /// <param name="selector">
    /// The selector.
    /// </param>
    /// <param name="sender">
    /// The button that was clicked.
    /// </param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ClickHandler(IntPtr self, IntPtr selector, IntPtr sender);

    /// <summary>
    /// Gets a value indicating whether a status item exists, so Avalonia's own tray icon is not needed.
    /// </summary>
    public static bool IsActive { get; private set; }

    /// <summary>
    /// Makes the status item, or tells that it cannot be made here.
    /// </summary>
    /// <param name="png">
    /// The icon as a PNG image.
    /// </param>
    /// <param name="toolTip">
    /// The tooltip.
    /// </param>
    /// <param name="clicked">
    /// Called on the UI thread when the item is clicked.
    /// </param>
    /// <returns>
    /// The item, or <see langword="null"/> where it cannot be made.
    /// </returns>
    public static MacStatusItem? TryCreate(byte[] png, string toolTip, Action clicked)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        try
        {
            s_clicked = clicked;
            if (s_target == IntPtr.Zero && (s_target = CreateTarget()) == IntPtr.Zero)
            {
                return null;
            }

            var bar = Send(GetClass("NSStatusBar"), Sel("systemStatusBar"));
            var item = SendDouble(bar, Sel("statusItemWithLength:"), VariableLength);
            if (bar == IntPtr.Zero || item == IntPtr.Zero)
            {
                return null;
            }

            // The status bar does not keep the item alive.
            Send(item, Sel("retain"));
            var button = Send(item, Sel("button"));
            var result = new MacStatusItem(item, button);
            try
            {
                if (button == IntPtr.Zero)
                {
                    result.Dispose();
                    return null;
                }

                SendPointer(button, Sel("setTarget:"), s_target);
                SendPointer(button, Sel("setAction:"), Sel("statusItemClicked:"));
                result.SetIcon(png);
                result.SetToolTip(toolTip);
            }
            catch
            {
                // An item that is left in the menu bar would be blank and could not be clicked.
                result.Dispose();
                throw;
            }

            IsActive = true;
            return result;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Brings the app to the front, which the list needs to take the focus: a click on the menu bar icon does not do it.
    /// </summary>
    public static void ActivateApp()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            SendBool(Send(GetClass("NSApplication"), Sel("sharedApplication")), Sel("activateIgnoringOtherApps:"), true);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // The list still opens, it just does not have the focus.
        }
    }

    /// <summary>
    /// Sets the tooltip.
    /// </summary>
    /// <param name="text">
    /// The tooltip text.
    /// </param>
    public void SetToolTip(string text)
    {
        if (_button != IntPtr.Zero)
        {
            SendPointer(_button, Sel("setToolTip:"), NSString(text));
        }
    }

    /// <summary>
    /// Shows or hides the item.
    /// </summary>
    /// <param name="visible">
    /// Whether the item shows in the menu bar.
    /// </param>
    public void SetVisible(bool visible)
    {
        if (_item != IntPtr.Zero)
        {
            SendBool(_item, Sel("setVisible:"), visible);
        }
    }

    /// <summary>
    /// Takes the item out of the menu bar.
    /// </summary>
    public void Dispose()
    {
        if (_item == IntPtr.Zero)
        {
            return;
        }

        SendPointer(Send(GetClass("NSStatusBar"), Sel("systemStatusBar")), Sel("removeStatusItem:"), _item);
        Send(_item, Sel("release"));
        _item = IntPtr.Zero;
        _button = IntPtr.Zero;
        IsActive = false;
    }

    /// <summary>
    /// Makes the class that receives the click and an instance of it.
    /// </summary>
    /// <returns>
    /// The instance, or zero when the class could not be made.
    /// </returns>
    private static IntPtr CreateTarget()
    {
        // The class stays registered for the life of the process, so a second attempt uses the one that exists.
        if (GetClass(TargetClassName) is var existing && existing != IntPtr.Zero)
        {
            // Without the handler of this process the class has no method to call, so a click would crash.
            return s_handler is null ? IntPtr.Zero : Send(Send(existing, Sel("alloc")), Sel("init"));
        }

        var type = AllocateClassPair(GetClass("NSObject"), TargetClassName, 0);
        if (type == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        s_handler = (_, _, _) => s_clicked?.Invoke();
        if (!AddMethod(type, Sel("statusItemClicked:"), Marshal.GetFunctionPointerForDelegate(s_handler), "v@:@"))
        {
            // A class that was never registered can be thrown away, so that the name is free for another try.
            DisposeClassPair(type);
            return IntPtr.Zero;
        }

        RegisterClassPair(type);
        return Send(Send(type, Sel("alloc")), Sel("init"));
    }

    /// <summary>
    /// Sets the image of the button.
    /// </summary>
    /// <param name="png">
    /// The image as PNG.
    /// </param>
    private void SetIcon(byte[] png)
    {
        var handle = GCHandle.Alloc(png, GCHandleType.Pinned);
        try
        {
            var data = SendBytes(GetClass("NSData"), Sel("dataWithBytes:length:"), handle.AddrOfPinnedObject(), (nuint)png.Length);
            var image = SendPointer(Send(GetClass("NSImage"), Sel("alloc")), Sel("initWithData:"), data);
            if (image == IntPtr.Zero)
            {
                // An item without an image or a title has no width and cannot be seen or clicked.
                SendPointer(_button, Sel("setTitle:"), NSString("FluxTick"));
                return;
            }

            SendSize(image, Sel("setSize:"), new CGSize { Width = IconSize, Height = IconSize });
            SendPointer(_button, Sel("setImage:"), image);

            // The button keeps its own reference to the image.
            Send(image, Sel("release"));
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>
    /// Makes an <c>NSString</c>.
    /// </summary>
    /// <param name="text">
    /// The text.
    /// </param>
    /// <returns>
    /// The string.
    /// </returns>
    private static IntPtr NSString(string text) => SendString(GetClass("NSString"), Sel("stringWithUTF8String:"), text);
}

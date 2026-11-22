using FluxTick.Core.Application;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FluxTick.App.Services;

/// <summary>
/// Listens for one hotkey that works in every program, through Carbon's <c>RegisterEventHotKey</c>. It needs no
/// accessibility permission, and the application event target delivers the press on the main run loop that Avalonia's
/// <c>NSApplication</c> already runs.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacGlobalHotkey : IGlobalHotkey
{
    /// <summary>
    /// The signature of this app's hotkeys, the four characters <c>FxTk</c>.
    /// </summary>
    private const uint Signature = 0x4678546B;

    /// <summary>
    /// The class of keyboard events (<c>kEventClassKeyboard</c>, <c>keyb</c>).
    /// </summary>
    private const uint EventClassKeyboard = 0x6B657962;

    /// <summary>
    /// The kind of event raised when a registered hotkey is pressed (<c>kEventHotKeyPressed</c>).
    /// </summary>
    private const uint EventHotKeyPressed = 5;

    /// <summary>
    /// The instances that listen, by the handler that serves them all.
    /// </summary>
    private static readonly List<MacGlobalHotkey> Instances = [];

    /// <summary>
    /// The handler of hotkey events, installed once for the application.
    /// </summary>
    private static IntPtr _handler;

    /// <summary>
    /// The managed handler; the field keeps the delegate alive for as long as Carbon may call it.
    /// </summary>
    private static readonly EventHandlerProc Callback = OnEvent;

    /// <summary>
    /// The identifier given to the next registration.
    /// </summary>
    private static uint _nextId;

    /// <summary>
    /// The reference of the registered hotkey, or zero when nothing is registered.
    /// </summary>
    private IntPtr _hotKey;

    /// <summary>
    /// The identifier of the registered hotkey.
    /// </summary>
    private uint _id;

    /// <inheritdoc/>
    public event EventHandler? Pressed;

    /// <inheritdoc/>
    public bool Register(HotkeyGesture gesture)
    {
        try
        {
            return RegisterCore(gesture);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Carbon cannot be loaded: the boss key is lost, but the app must still start.
            return false;
        }
    }

    /// <inheritdoc/>
    public void Unregister()
    {
        try
        {
            UnregisterCore();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _hotKey = IntPtr.Zero;
        }
    }

    /// <inheritdoc/>
    public void Dispose() => Unregister();

    /// <summary>
    /// Registers a hotkey with Carbon, replacing the one registered before.
    /// </summary>
    /// <param name="gesture">
    /// The hotkey.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when Carbon accepted it.
    /// </returns>
    private bool RegisterCore(HotkeyGesture gesture)
    {
        UnregisterCore();

        var code = MacKeyCodes.KeyCode(gesture);
        if (code < 0 || !EnsureHandler())
        {
            return false;
        }

        var id = ++_nextId;
        var hotKeyId = new EventHotKeyId { Signature = Signature, Id = id };
        var status = RegisterEventHotKey((uint)code, MacKeyCodes.ModifierFlags(gesture.Modifiers), hotKeyId, GetApplicationEventTarget(), 0, out var reference);
        if (status != 0)
        {
            return false;
        }

        _hotKey = reference;
        _id = id;
        lock (Instances)
        {
            Instances.Add(this);
        }

        return true;
    }

    /// <summary>
    /// Gives the hotkey back to Carbon.
    /// </summary>
    private void UnregisterCore()
    {
        if (_hotKey == IntPtr.Zero)
        {
            return;
        }

        lock (Instances)
        {
            Instances.Remove(this);
        }

        UnregisterEventHotKey(_hotKey);
        _hotKey = IntPtr.Zero;
    }

    /// <summary>
    /// Installs the event handler the first time a hotkey is registered.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the handler is installed.
    /// </returns>
    private static bool EnsureHandler()
    {
        if (_handler != IntPtr.Zero)
        {
            return true;
        }

        var spec = new EventTypeSpec { EventClass = EventClassKeyboard, EventKind = EventHotKeyPressed };
        var status = InstallEventHandler(GetApplicationEventTarget(), Callback, 1, ref spec, IntPtr.Zero, out _handler);
        if (status == 0)
        {
            return true;
        }

        _handler = IntPtr.Zero;
        return false;
    }

    /// <summary>
    /// Serves a hotkey event: finds which hotkey was pressed and raises its event.
    /// </summary>
    /// <param name="next">
    /// The next handler in the chain.
    /// </param>
    /// <param name="eventRef">
    /// The event.
    /// </param>
    /// <param name="userData">
    /// Unused.
    /// </param>
    /// <returns>
    /// Zero when the event was handled, or <c>eventNotHandledErr</c> when it is not one of ours.
    /// </returns>
    private static int OnEvent(IntPtr next, IntPtr eventRef, IntPtr userData)
    {
        const int NotHandled = -9874;
        try
        {
            // kEventParamDirectObject ('----') of type kEventParamEventHotKeyID ('hkid').
            var status = GetEventParameter(eventRef, 0x2D2D2D2D, 0x686B6964, IntPtr.Zero, 8, IntPtr.Zero, out EventHotKeyId hotKeyId);
            if (status != 0 || hotKeyId.Signature != Signature)
            {
                return NotHandled;
            }

            MacGlobalHotkey[] listeners;
            lock (Instances)
            {
                listeners = Instances.Where(instance => instance._id == hotKeyId.Id).ToArray();
            }

            foreach (var listener in listeners)
            {
                listener.Pressed?.Invoke(listener, EventArgs.Empty);
            }

            return listeners.Length > 0 ? 0 : NotHandled;
        }
        catch
        {
            // An exception must not unwind into native code.
            return NotHandled;
        }
    }

    /// <summary>
    /// The signature of a Carbon event handler.
    /// </summary>
    /// <param name="next">
    /// The next handler in the chain.
    /// </param>
    /// <param name="eventRef">
    /// The event.
    /// </param>
    /// <param name="userData">
    /// The data given at installation.
    /// </param>
    /// <returns>
    /// The status.
    /// </returns>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EventHandlerProc(IntPtr next, IntPtr eventRef, IntPtr userData);

    /// <summary>
    /// The identifier of a hotkey: the signature of its owner and a number.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyId
    {
        /// <summary>
        /// The signature of the owner.
        /// </summary>
        public uint Signature;

        /// <summary>
        /// The number of the hotkey.
        /// </summary>
        public uint Id;
    }

    /// <summary>
    /// Names a kind of event to listen for.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        /// <summary>
        /// The class of the event.
        /// </summary>
        public uint EventClass;

        /// <summary>
        /// The kind of the event.
        /// </summary>
        public uint EventKind;
    }

    /// <summary>
    /// Gets the event target of the application.
    /// </summary>
    /// <returns>
    /// The target.
    /// </returns>
    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern IntPtr GetApplicationEventTarget();

    /// <summary>
    /// Installs a handler for a kind of event on a target.
    /// </summary>
    /// <param name="target">The event target.</param>
    /// <param name="handler">The handler.</param>
    /// <param name="count">The number of event types.</param>
    /// <param name="types">The event types to handle.</param>
    /// <param name="userData">Data passed to the handler.</param>
    /// <param name="reference">The reference of the installed handler.</param>
    /// <returns>
    /// The status, zero when it succeeded.
    /// </returns>
    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern int InstallEventHandler(IntPtr target, EventHandlerProc handler, uint count, ref EventTypeSpec types, IntPtr userData, out IntPtr reference);

    /// <summary>
    /// Registers a hotkey.
    /// </summary>
    /// <param name="keyCode">The virtual key code.</param>
    /// <param name="modifiers">The Carbon modifier flags.</param>
    /// <param name="id">The identifier of the hotkey.</param>
    /// <param name="target">The event target that receives the press.</param>
    /// <param name="options">The options, zero for none.</param>
    /// <param name="reference">The reference of the registered hotkey.</param>
    /// <returns>
    /// The status, zero when it succeeded.
    /// </returns>
    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyId id, IntPtr target, uint options, out IntPtr reference);

    /// <summary>
    /// Unregisters a hotkey.
    /// </summary>
    /// <param name="reference">The reference of the hotkey.</param>
    /// <returns>
    /// The status, zero when it succeeded.
    /// </returns>
    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern int UnregisterEventHotKey(IntPtr reference);

    /// <summary>
    /// Reads a parameter of an event.
    /// </summary>
    /// <param name="eventRef">The event.</param>
    /// <param name="name">The name of the parameter.</param>
    /// <param name="type">The type of the parameter.</param>
    /// <param name="actualType">Receives the actual type, or zero.</param>
    /// <param name="size">The size of the buffer.</param>
    /// <param name="actualSize">Receives the actual size, or zero.</param>
    /// <param name="data">The buffer.</param>
    /// <returns>
    /// The status, zero when it succeeded.
    /// </returns>
    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon")]
    private static extern int GetEventParameter(IntPtr eventRef, uint name, uint type, IntPtr actualType, uint size, IntPtr actualSize, out EventHotKeyId data);
}

using FluxTick.Core.Application;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FluxTick.App.Services;

/// <summary>
/// Listens for one hotkey that works in every program. Windows delivers the hotkey to the thread that registered it, so
/// that thread runs a message loop of its own; <see cref="Pressed"/> is raised on it, not on the UI thread.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsGlobalHotkey : IDisposable
{
    /// <summary>
    /// Keeps a held key from raising the hotkey again and again.
    /// </summary>
    private const uint NoRepeat = 0x4000;

    /// <summary>
    /// The message that tells a message loop to end.
    /// </summary>
    private const uint QuitMessage = 0x0012;

    /// <summary>
    /// The message Windows sends when a registered hotkey is pressed.
    /// </summary>
    private const uint HotkeyMessage = 0x0312;

    /// <summary>
    /// The identifier of the hotkey on its thread.
    /// </summary>
    private const int HotkeyId = 1;

    /// <summary>
    /// The thread that holds the registration, or <see langword="null"/> when nothing is registered.
    /// </summary>
    private Thread? _thread;

    /// <summary>
    /// The native identifier of <see cref="_thread"/>.
    /// </summary>
    private uint _threadId;

    /// <summary>
    /// Occurs when the hotkey is pressed, on a thread of its own.
    /// </summary>
    public event EventHandler? Pressed;

    /// <summary>
    /// Registers a hotkey, replacing the one registered before.
    /// </summary>
    /// <param name="gesture">
    /// The hotkey.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when Windows accepted it; <see langword="false"/> when another program holds it.
    /// </returns>
    public bool Register(HotkeyGesture gesture)
    {
        Unregister();

        using var ready = new ManualResetEventSlim();
        var registered = false;
        uint threadId = 0;
        var thread = new Thread(() =>
        {
            threadId = GetCurrentThreadId();

            // Windows makes the message queue of a thread when it first looks at it; until then a message posted to the
            // thread is lost, and so would be the quit message of Unregister.
            PeekMessage(out _, IntPtr.Zero, 0x0400, 0x0400, 0);
            registered = RegisterHotKey(IntPtr.Zero, HotkeyId, WindowsKeyCodes.ModifierFlags(gesture.Modifiers) | NoRepeat, (uint)WindowsKeyCodes.VirtualKey(gesture));
            ready.Set();
            if (!registered)
            {
                return;
            }

            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.Message == HotkeyMessage)
                {
                    Pressed?.Invoke(this, EventArgs.Empty);
                }
            }

            UnregisterHotKey(IntPtr.Zero, HotkeyId);
        })
        {
            IsBackground = true,
            Name = "FluxTick boss key",
        };
        thread.Start();
        ready.Wait();

        if (!registered)
        {
            return false;
        }

        _thread = thread;
        _threadId = threadId;
        return true;
    }

    /// <summary>
    /// Gives the hotkey back, so other programs can use it.
    /// </summary>
    public void Unregister()
    {
        if (_thread is null)
        {
            return;
        }

        PostThreadMessage(_threadId, QuitMessage, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(1));
        _thread = null;
    }

    /// <inheritdoc/>
    public void Dispose() => Unregister();

    /// <summary>
    /// Registers a hotkey for the calling thread.
    /// </summary>
    /// <param name="window">
    /// The window that receives the message, or zero for the thread.
    /// </param>
    /// <param name="id">
    /// The identifier of the hotkey.
    /// </param>
    /// <param name="modifiers">
    /// The modifier keys.
    /// </param>
    /// <param name="key">
    /// The virtual key code.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when it succeeded.
    /// </returns>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    /// <summary>
    /// Removes a hotkey of the calling thread.
    /// </summary>
    /// <param name="window">
    /// The window that received the message, or zero.
    /// </param>
    /// <param name="id">
    /// The identifier of the hotkey.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when it succeeded.
    /// </returns>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    /// <summary>
    /// Waits for the next message of the calling thread.
    /// </summary>
    /// <param name="message">
    /// The message.
    /// </param>
    /// <param name="window">
    /// Zero for every window of the thread.
    /// </param>
    /// <param name="min">
    /// The lowest message to take, or zero.
    /// </param>
    /// <param name="max">
    /// The highest message to take, or zero.
    /// </param>
    /// <returns>
    /// Above zero for a message, zero for the quit message.
    /// </returns>
    [DllImport("user32.dll")]
    private static extern int GetMessage(out NativeMessage message, IntPtr window, uint min, uint max);

    /// <summary>
    /// Looks at the next message of the calling thread without waiting, which makes sure the thread has a queue.
    /// </summary>
    /// <param name="message">
    /// The message.
    /// </param>
    /// <param name="window">
    /// Zero for every window of the thread.
    /// </param>
    /// <param name="min">
    /// The lowest message to take.
    /// </param>
    /// <param name="max">
    /// The highest message to take.
    /// </param>
    /// <param name="remove">
    /// Whether the message is taken off the queue.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when there is a message.
    /// </returns>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint min, uint max, uint remove);

    /// <summary>
    /// Posts a message to the message loop of a thread.
    /// </summary>
    /// <param name="thread">
    /// The native thread identifier.
    /// </param>
    /// <param name="message">
    /// The message.
    /// </param>
    /// <param name="wParam">
    /// The first parameter.
    /// </param>
    /// <param name="lParam">
    /// The second parameter.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when it succeeded.
    /// </returns>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Gets the native identifier of the calling thread.
    /// </summary>
    /// <returns>
    /// The identifier.
    /// </returns>
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    /// <summary>
    /// The Win32 MSG.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        /// <summary>
        /// The window.
        /// </summary>
        public IntPtr Window;

        /// <summary>
        /// The message.
        /// </summary>
        public uint Message;

        /// <summary>
        /// The first parameter.
        /// </summary>
        public IntPtr WParam;

        /// <summary>
        /// The second parameter.
        /// </summary>
        public IntPtr LParam;

        /// <summary>
        /// The time of the message.
        /// </summary>
        public uint Time;

        /// <summary>
        /// The pointer position when the message was posted, x.
        /// </summary>
        public int X;

        /// <summary>
        /// The pointer position when the message was posted, y.
        /// </summary>
        public int Y;
    }
}

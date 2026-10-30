using System.Runtime.InteropServices;

namespace FluxTick.App.Services;

/// <summary>
/// The calls into the Objective-C runtime of macOS that the app makes, shared by the pieces that talk to AppKit.
/// </summary>
internal static class ObjC
{
    /// <summary>
    /// The Objective-C runtime library.
    /// </summary>
    private const string Library = "/usr/lib/libobjc.A.dylib";

    /// <summary>
    /// Gets the Objective-C class with the given name.
    /// </summary>
    /// <param name="name">
    /// The class name.
    /// </param>
    /// <returns>
    /// The class, or zero.
    /// </returns>
    [DllImport(Library, EntryPoint = "objc_getClass")]
    public static extern IntPtr GetClass(string name);

    /// <summary>
    /// Gets the selector with the given name.
    /// </summary>
    /// <param name="name">
    /// The selector name.
    /// </param>
    /// <returns>
    /// The selector.
    /// </returns>
    [DllImport(Library, EntryPoint = "sel_registerName")]
    public static extern IntPtr Sel(string name);

    /// <summary>
    /// Starts a new class below the given one, or answers zero when the name is taken.
    /// </summary>
    /// <param name="superclass">
    /// The class to derive from.
    /// </param>
    /// <param name="name">
    /// The name of the new class.
    /// </param>
    /// <param name="extraBytes">
    /// The extra bytes of each instance.
    /// </param>
    /// <returns>
    /// The new class.
    /// </returns>
    [DllImport(Library, EntryPoint = "objc_allocateClassPair")]
    public static extern IntPtr AllocateClassPair(IntPtr superclass, string name, nuint extraBytes);

    /// <summary>
    /// Makes a class started with <see cref="AllocateClassPair"/> usable.
    /// </summary>
    /// <param name="type">
    /// The class.
    /// </param>
    [DllImport(Library, EntryPoint = "objc_registerClassPair")]
    public static extern void RegisterClassPair(IntPtr type);

    /// <summary>
    /// Throws away a class started with <see cref="AllocateClassPair"/> that was not registered.
    /// </summary>
    /// <param name="type">
    /// The class.
    /// </param>
    [DllImport(Library, EntryPoint = "objc_disposeClassPair")]
    public static extern void DisposeClassPair(IntPtr type);

    /// <summary>
    /// Adds a method to a class that is not registered yet.
    /// </summary>
    /// <param name="type">
    /// The class.
    /// </param>
    /// <param name="selector">
    /// The selector of the method.
    /// </param>
    /// <param name="implementation">
    /// The function pointer of the method.
    /// </param>
    /// <param name="types">
    /// The type encoding of the method.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the method was added.
    /// </returns>
    [DllImport(Library, EntryPoint = "class_addMethod")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool AddMethod(IntPtr type, IntPtr selector, IntPtr implementation, string types);

    /// <summary>
    /// Sends a message without arguments that answers an object.
    /// </summary>
    /// <param name="receiver">
    /// The receiver.
    /// </param>
    /// <param name="selector">
    /// The selector.
    /// </param>
    /// <returns>
    /// The answer.
    /// </returns>
    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    /// <summary>
    /// Sends a message with one object argument. A message that answers nothing leaves the result meaningless.
    /// </summary>
    /// <param name="receiver">
    /// The receiver.
    /// </param>
    /// <param name="selector">
    /// The selector.
    /// </param>
    /// <param name="argument">
    /// The argument.
    /// </param>
    /// <returns>
    /// The answer.
    /// </returns>
    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendPointer(IntPtr receiver, IntPtr selector, IntPtr argument);

    /// <summary>
    /// Sends a message with one floating point argument.
    /// </summary>
    /// <param name="receiver">
    /// The receiver.
    /// </param>
    /// <param name="selector">
    /// The selector.
    /// </param>
    /// <param name="argument">
    /// The argument.
    /// </param>
    /// <returns>
    /// The answer.
    /// </returns>
    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendDouble(IntPtr receiver, IntPtr selector, double argument);

    /// <summary>
    /// Sends a message with one boolean argument.
    /// </summary>
    /// <param name="receiver">
    /// The receiver.
    /// </param>
    /// <param name="selector">
    /// The selector.
    /// </param>
    /// <param name="argument">
    /// The argument.
    /// </param>
    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern void SendBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.U1)] bool argument);

    /// <summary>
    /// Sends a message with one unsigned integer argument.
    /// </summary>
    /// <param name="receiver">
    /// The receiver.
    /// </param>
    /// <param name="selector">
    /// The selector.
    /// </param>
    /// <param name="argument">
    /// The argument.
    /// </param>
    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern void SendUIntArgument(IntPtr receiver, IntPtr selector, ulong argument);

    /// <summary>
    /// Sends a message without arguments that answers an unsigned integer.
    /// </summary>
    /// <param name="receiver">
    /// The receiver.
    /// </param>
    /// <param name="selector">
    /// The selector.
    /// </param>
    /// <returns>
    /// The answer.
    /// </returns>
    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern ulong SendUInt(IntPtr receiver, IntPtr selector);

    /// <summary>
    /// Sends a message with a pointer to bytes and their count.
    /// </summary>
    /// <param name="receiver">
    /// The receiver.
    /// </param>
    /// <param name="selector">
    /// The selector.
    /// </param>
    /// <param name="bytes">
    /// The pointer to the bytes.
    /// </param>
    /// <param name="length">
    /// The number of bytes.
    /// </param>
    /// <returns>
    /// The answer.
    /// </returns>
    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendBytes(IntPtr receiver, IntPtr selector, IntPtr bytes, nuint length);

    /// <summary>
    /// Sends a message with one UTF-8 string argument.
    /// </summary>
    /// <param name="receiver">
    /// The receiver.
    /// </param>
    /// <param name="selector">
    /// The selector.
    /// </param>
    /// <param name="argument">
    /// The argument.
    /// </param>
    /// <returns>
    /// The answer.
    /// </returns>
    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendString(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string argument);

    /// <summary>
    /// Sends a message with one size argument. A size is two doubles, which both the arm64 and the x86-64 conventions pass
    /// in registers, so the plain send is right.
    /// </summary>
    /// <param name="receiver">
    /// The receiver.
    /// </param>
    /// <param name="selector">
    /// The selector.
    /// </param>
    /// <param name="argument">
    /// The size.
    /// </param>
    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern void SendSize(IntPtr receiver, IntPtr selector, CGSize argument);

    /// <summary>
    /// Sends a message that answers a point. A point is two doubles, which both the arm64 and the x86-64 conventions return
    /// in registers, so the plain send is right.
    /// </summary>
    /// <param name="receiver">
    /// The receiver.
    /// </param>
    /// <param name="selector">
    /// The selector.
    /// </param>
    /// <returns>
    /// The point.
    /// </returns>
    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern CGPoint SendPoint(IntPtr receiver, IntPtr selector);

    /// <summary>
    /// A size of Cocoa.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CGSize
    {
        /// <summary>
        /// The width.
        /// </summary>
        public double Width;

        /// <summary>
        /// The height.
        /// </summary>
        public double Height;
    }

    /// <summary>
    /// A point of Cocoa.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CGPoint
    {
        /// <summary>
        /// The horizontal position.
        /// </summary>
        public double X;

        /// <summary>
        /// The vertical position.
        /// </summary>
        public double Y;
    }
}

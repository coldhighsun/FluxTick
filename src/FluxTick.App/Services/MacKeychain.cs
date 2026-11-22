using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FluxTick.App.Services;

/// <summary>
/// The generic passwords of the macOS Keychain that the app uses, through the <c>SecItem</c> functions of Security.framework.
/// Only what the app needs is here: reading one item and adding one item, found by service and account.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacKeychain
{
    /// <summary>
    /// The result of a call that went well (<c>errSecSuccess</c>).
    /// </summary>
    public const int Success = 0;

    /// <summary>
    /// The result when no item matches (<c>errSecItemNotFound</c>).
    /// </summary>
    public const int ItemNotFound = -25300;

    /// <summary>
    /// The result when an item with the same service and account exists already (<c>errSecDuplicateItem</c>).
    /// </summary>
    public const int DuplicateItem = -25299;

    /// <summary>
    /// The Security framework.
    /// </summary>
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";

    /// <summary>
    /// The CoreFoundation framework.
    /// </summary>
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    /// <summary>
    /// The UTF-8 encoding of <c>CFStringCreateWithCString</c> (<c>kCFStringEncodingUTF8</c>).
    /// </summary>
    private const uint Utf8 = 0x08000100;

    /// <summary>
    /// The frameworks that were loaded, by path, so that each is loaded once and its handle is not taken again per call.
    /// </summary>
    private static readonly ConcurrentDictionary<string, IntPtr> Libraries = new();

    /// <summary>
    /// The constants that the frameworks export, by name. They never change while the process runs.
    /// </summary>
    private static readonly ConcurrentDictionary<string, IntPtr> Constants = new();

    /// <summary>
    /// Reads the data of a generic password.
    /// </summary>
    /// <param name="service">
    /// The service of the item.
    /// </param>
    /// <param name="account">
    /// The account of the item.
    /// </param>
    /// <param name="data">
    /// The data, or an empty array when there is none.
    /// </param>
    /// <returns>
    /// <c>errSecSuccess</c> (<see cref="Success"/>) when the item was read; <see cref="ItemNotFound"/> when there is none;
    /// another status when the Keychain refused (locked, access denied by the user, and so on).
    /// </returns>
    public static int TryGet(string service, string account, out byte[] data)
    {
        data = [];
        var created = new List<IntPtr>();
        try
        {
            var query = Dictionary(
                created,
                (Constant("kSecClass"), Constant("kSecClassGenericPassword")),
                (Constant("kSecAttrService"), Text(created, service)),
                (Constant("kSecAttrAccount"), Text(created, account)),
                (Constant("kSecReturnData"), Constant("kCFBooleanTrue")),
                (Constant("kSecMatchLimit"), Constant("kSecMatchLimitOne")));

            var status = SecItemCopyMatching(query, out var result);
            if (status != Success)
            {
                return status;
            }

            created.Add(result);
            var length = checked((int)CFDataGetLength(result));
            data = new byte[length];
            Marshal.Copy(CFDataGetBytePtr(result), data, 0, length);
            return Success;
        }
        finally
        {
            Release(created);
        }
    }

    /// <summary>
    /// Adds a generic password. The Keychain keeps it for the login of the user and does not replace one that exists.
    /// </summary>
    /// <param name="service">
    /// The service of the item.
    /// </param>
    /// <param name="account">
    /// The account of the item.
    /// </param>
    /// <param name="data">
    /// The data to keep.
    /// </param>
    /// <returns>
    /// <c>errSecSuccess</c> (<see cref="Success"/>) when the item was added; <see cref="DuplicateItem"/> when one exists;
    /// another status when the Keychain refused.
    /// </returns>
    public static int TryAdd(string service, string account, byte[] data)
    {
        var created = new List<IntPtr>();
        try
        {
            var value = CFDataCreate(IntPtr.Zero, data, data.Length);
            created.Add(value);
            var attributes = Dictionary(
                created,
                (Constant("kSecClass"), Constant("kSecClassGenericPassword")),
                (Constant("kSecAttrService"), Text(created, service)),
                (Constant("kSecAttrAccount"), Text(created, account)),
                (Constant("kSecValueData"), value));

            return SecItemAdd(attributes, IntPtr.Zero);
        }
        finally
        {
            Release(created);
        }
    }

    /// <summary>
    /// Makes a CoreFoundation dictionary that keeps its keys and values.
    /// </summary>
    /// <param name="created">
    /// The objects to release when the call is done; the dictionary is added to them.
    /// </param>
    /// <param name="entries">
    /// The keys and values.
    /// </param>
    /// <returns>
    /// The dictionary.
    /// </returns>
    private static IntPtr Dictionary(List<IntPtr> created, params (IntPtr Key, IntPtr Value)[] entries)
    {
        var keys = entries.Select(e => e.Key).ToArray();
        var values = entries.Select(e => e.Value).ToArray();
        var dictionary = CFDictionaryCreate(
            IntPtr.Zero,
            keys,
            values,
            entries.Length,
            Export(CoreFoundation, "kCFTypeDictionaryKeyCallBacks"),
            Export(CoreFoundation, "kCFTypeDictionaryValueCallBacks"));
        created.Add(dictionary);
        return dictionary;
    }

    /// <summary>
    /// Makes a CoreFoundation string.
    /// </summary>
    /// <param name="created">
    /// The objects to release when the call is done; the string is added to them.
    /// </param>
    /// <param name="text">
    /// The text.
    /// </param>
    /// <returns>
    /// The string.
    /// </returns>
    private static IntPtr Text(List<IntPtr> created, string text)
    {
        var value = CFStringCreateWithCString(IntPtr.Zero, text, Utf8);
        created.Add(value);
        return value;
    }

    /// <summary>
    /// Gets the value of a constant that a framework exports, such as <c>kSecClass</c>. The framework exports a variable
    /// that holds the object, so the address it gives is read once.
    /// </summary>
    /// <param name="name">
    /// The name of the constant.
    /// </param>
    /// <returns>
    /// The object.
    /// </returns>
    private static IntPtr Constant(string name) =>
        Constants.GetOrAdd(
            name,
            n => Marshal.ReadIntPtr(Export(n.StartsWith("kCF", StringComparison.Ordinal) ? CoreFoundation : Security, n)));

    /// <summary>
    /// Gets the address of a symbol that a framework exports.
    /// </summary>
    /// <param name="library">
    /// The framework.
    /// </param>
    /// <param name="name">
    /// The name of the symbol.
    /// </param>
    /// <returns>
    /// The address.
    /// </returns>
    private static IntPtr Export(string library, string name) =>
        NativeLibrary.GetExport(Libraries.GetOrAdd(library, NativeLibrary.Load), name);

    /// <summary>
    /// Releases CoreFoundation objects that were made by this class.
    /// </summary>
    /// <param name="objects">
    /// The objects.
    /// </param>
    private static void Release(List<IntPtr> objects)
    {
        foreach (var item in objects)
        {
            if (item != IntPtr.Zero)
            {
                CFRelease(item);
            }
        }
    }

    /// <summary>
    /// Finds the item that matches a query.
    /// </summary>
    /// <param name="query">
    /// The attributes to match.
    /// </param>
    /// <param name="result">
    /// The data of the item, which the caller releases.
    /// </param>
    /// <returns>
    /// The status of the call.
    /// </returns>
    [DllImport(Security)]
    private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);

    /// <summary>
    /// Adds an item to the Keychain.
    /// </summary>
    /// <param name="attributes">
    /// The attributes and the data of the item.
    /// </param>
    /// <param name="result">
    /// Not used; zero.
    /// </param>
    /// <returns>
    /// The status of the call.
    /// </returns>
    [DllImport(Security)]
    private static extern int SecItemAdd(IntPtr attributes, IntPtr result);

    /// <summary>
    /// Makes a CoreFoundation dictionary.
    /// </summary>
    /// <param name="allocator">
    /// The allocator; zero for the default.
    /// </param>
    /// <param name="keys">
    /// The keys.
    /// </param>
    /// <param name="values">
    /// The values.
    /// </param>
    /// <param name="count">
    /// The number of entries.
    /// </param>
    /// <param name="keyCallbacks">
    /// How the keys are kept.
    /// </param>
    /// <param name="valueCallbacks">
    /// How the values are kept.
    /// </param>
    /// <returns>
    /// The dictionary, which the caller releases.
    /// </returns>
    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDictionaryCreate(
        IntPtr allocator,
        IntPtr[] keys,
        IntPtr[] values,
        nint count,
        IntPtr keyCallbacks,
        IntPtr valueCallbacks);

    /// <summary>
    /// Makes a CoreFoundation string.
    /// </summary>
    /// <param name="allocator">
    /// The allocator; zero for the default.
    /// </param>
    /// <param name="text">
    /// The text.
    /// </param>
    /// <param name="encoding">
    /// The encoding of the text.
    /// </param>
    /// <returns>
    /// The string, which the caller releases.
    /// </returns>
    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringCreateWithCString(
        IntPtr allocator,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
        uint encoding);

    /// <summary>
    /// Makes a CoreFoundation data object that copies bytes.
    /// </summary>
    /// <param name="allocator">
    /// The allocator; zero for the default.
    /// </param>
    /// <param name="bytes">
    /// The bytes.
    /// </param>
    /// <param name="length">
    /// The number of bytes.
    /// </param>
    /// <returns>
    /// The data, which the caller releases.
    /// </returns>
    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);

    /// <summary>
    /// Gets the address of the bytes of a data object.
    /// </summary>
    /// <param name="data">
    /// The data.
    /// </param>
    /// <returns>
    /// The address, valid while the data is.
    /// </returns>
    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDataGetBytePtr(IntPtr data);

    /// <summary>
    /// Gets the number of bytes of a data object.
    /// </summary>
    /// <param name="data">
    /// The data.
    /// </param>
    /// <returns>
    /// The number of bytes.
    /// </returns>
    [DllImport(CoreFoundation)]
    private static extern nint CFDataGetLength(IntPtr data);

    /// <summary>
    /// Gives up a reference to a CoreFoundation object.
    /// </summary>
    /// <param name="value">
    /// The object.
    /// </param>
    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr value);
}

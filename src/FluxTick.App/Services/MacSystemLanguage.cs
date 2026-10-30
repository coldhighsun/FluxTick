using System.Globalization;
using System.Runtime.InteropServices;
using static FluxTick.App.Services.ObjC;

namespace FluxTick.App.Services;

/// <summary>
/// Asks macOS for the language the user prefers. .NET takes its UI culture from the <c>LANG</c> variable first, which a
/// terminal or an IDE often sets to English whatever the system is set to.
/// </summary>
public static class MacSystemLanguage
{
    /// <summary>
    /// The Foundation framework, which has to be loaded for its classes to exist.
    /// </summary>
    private const string Foundation = "/System/Library/Frameworks/Foundation.framework/Foundation";

    /// <summary>
    /// Gets the culture of the first preferred language of the user.
    /// </summary>
    /// <returns>
    /// The culture, or <see langword="null"/> where it cannot be told.
    /// </returns>
    public static CultureInfo? TryGetPreferred()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        try
        {
            NativeLibrary.Load(Foundation);
            var languages = Send(GetClass("NSLocale"), Sel("preferredLanguages"));
            var first = Send(languages, Sel("firstObject"));
            if (first == IntPtr.Zero)
            {
                return null;
            }

            var name = Marshal.PtrToStringUTF8(Send(first, Sel("UTF8String")));
            return string.IsNullOrEmpty(name) ? null : CultureInfo.GetCultureInfo(name);
        }
        catch (Exception)
        {
            // Runs in a static initializer of Loc, where any failure would take the whole localization down; the culture of
            // the process is a good enough answer.
            return null;
        }
    }
}

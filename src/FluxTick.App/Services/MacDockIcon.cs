using static FluxTick.App.Services.ObjC;

namespace FluxTick.App.Services;

/// <summary>
/// Shows or removes the icon of the app in the Dock of macOS, which it does through the activation policy of the app.
/// </summary>
public static class MacDockIcon
{
    /// <summary>
    /// The activation policy of an app with a Dock icon and a menu bar of its own.
    /// </summary>
    private const ulong RegularPolicy = 0;

    /// <summary>
    /// The activation policy of an app that has no Dock icon and no menu bar of its own, which keeps its windows and its
    /// status item.
    /// </summary>
    private const ulong AccessoryPolicy = 1;

    /// <summary>
    /// Shows or removes the app icon in the Dock; without it the icon in the menu bar is the only one.
    /// </summary>
    /// <param name="visible">
    /// <see langword="true"/> to keep the icon in the Dock, <see langword="false"/> to remove it.
    /// </param>
    public static void SetVisible(bool visible)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            var application = Send(GetClass("NSApplication"), Sel("sharedApplication"));
            SendUIntArgument(application, Sel("setActivationPolicy:"), visible ? RegularPolicy : AccessoryPolicy);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // The Dock keeps the icon it has.
        }
    }
}

namespace FluxTick.App.Services;

/// <summary>
/// Tells whether listing the instruments while the pointer rests on the tray icon can work here, so the setting for it is
/// only offered when it can.
/// </summary>
public static class TrayHoverAvailability
{
    /// <summary>
    /// Whether the hover watcher could not be started although the platform supports it: 1 when it could not, otherwise
    /// 0. It is exchanged atomically, as the watcher reports from other threads than the one that reads it.
    /// </summary>
    private static int s_failed;

    /// <summary>
    /// Gets a value indicating whether hovering the tray icon can list the instruments: only Windows can tell where the
    /// icon is, and only while the watcher could be started.
    /// </summary>
    public static bool IsSupported => OperatingSystem.IsWindows() && Volatile.Read(ref s_failed) == 0;

    /// <summary>
    /// Occurs when <see cref="IsSupported"/> changed, on the thread that reported it.
    /// </summary>
    public static event EventHandler? Changed;

    /// <summary>
    /// Gets or sets what is told about an exception that a <see cref="Changed"/> listener threw, for example to log it.
    /// </summary>
    public static Action<Exception>? ListenerFailed { get; set; }

    /// <summary>
    /// Records that the watcher could not be started, after all attempts.
    /// </summary>
    public static void ReportFailure() => Set(failed: true);

    /// <summary>
    /// Records that the watcher is running.
    /// </summary>
    public static void ReportSuccess() => Set(failed: false);

    /// <summary>
    /// Stores the state and tells the listeners when it is a change.
    /// </summary>
    /// <param name="failed">
    /// Whether the watcher could not be started.
    /// </param>
    private static void Set(bool failed)
    {
        // Only the call that really changes the state announces it, however many report at the same time.
        if (Interlocked.Exchange(ref s_failed, failed ? 1 : 0) == (failed ? 1 : 0))
        {
            return;
        }

        // The listeners know nothing of each other nor of the watcher that reports, so one that fails must neither keep
        // the others from hearing of it nor break the watcher.
        foreach (var listener in Changed?.GetInvocationList() ?? [])
        {
            try
            {
                ((EventHandler)listener)(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                // There is nothing the watcher could do about a broken listener, but whoever runs the app should hear of it.
                Report(ex);
            }
        }
    }

    /// <summary>
    /// Hands an exception of a listener to <see cref="ListenerFailed"/>. A hook that fails too has nobody left to tell.
    /// </summary>
    /// <param name="exception">
    /// The exception the listener threw.
    /// </param>
    private static void Report(Exception exception)
    {
        try
        {
            ListenerFailed?.Invoke(exception);
        }
        catch (Exception)
        {
            // Reporting must never break the watcher that is announcing the change.
        }
    }
}

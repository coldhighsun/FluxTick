namespace FluxTick.App.Services;

/// <summary>
/// Decides from samples of the pointer when the tray popup opens and closes. The pointer must rest on the icon for
/// <see cref="EnterSamples"/> samples in a row to open it, and stay away from both the icon and the popup for
/// <see cref="LeaveSamples"/> samples in a row to close it, so passing over the icon does not flash the popup and moving
/// from the icon to the popup does not close it.
/// </summary>
public sealed class TrayHoverTracker
{
    /// <summary>
    /// The samples on the icon, in a row, that open the popup.
    /// </summary>
    public const int EnterSamples = 2;

    /// <summary>
    /// The samples away from the icon and the popup, in a row, that close the popup.
    /// </summary>
    public const int LeaveSamples = 2;

    /// <summary>
    /// The samples on the icon so far.
    /// </summary>
    private int _inside;

    /// <summary>
    /// The samples away from the icon and the popup so far.
    /// </summary>
    private int _outside;

    /// <summary>
    /// Whether the popup was dismissed and stays closed until the pointer has left the icon once.
    /// </summary>
    private bool _suppressed;

    /// <summary>
    /// What a sample asks for.
    /// </summary>
    public enum Step
    {
        /// <summary>
        /// Nothing changes.
        /// </summary>
        None,

        /// <summary>
        /// The popup opens.
        /// </summary>
        Open,

        /// <summary>
        /// The popup closes.
        /// </summary>
        Close,
    }

    /// <summary>
    /// Gets a value indicating whether the popup is open.
    /// </summary>
    public bool IsOpen { get; private set; }

    /// <summary>
    /// Takes one sample of the pointer.
    /// </summary>
    /// <param name="onIcon">
    /// Whether the pointer is on the tray icon.
    /// </param>
    /// <param name="onPopup">
    /// Whether the pointer is on the popup.
    /// </param>
    /// <returns>
    /// What the sample asks for.
    /// </returns>
    public Step Update(bool onIcon, bool onPopup)
    {
        if (onIcon)
        {
            _outside = 0;
            if (_suppressed || IsOpen || ++_inside < EnterSamples)
            {
                return Step.None;
            }

            _inside = 0;
            IsOpen = true;
            return Step.Open;
        }

        _inside = 0;
        _suppressed = false;
        if (!IsOpen)
        {
            return Step.None;
        }

        if (onPopup)
        {
            _outside = 0;
            return Step.None;
        }

        if (++_outside < LeaveSamples)
        {
            return Step.None;
        }

        _outside = 0;
        IsOpen = false;
        return Step.Close;
    }

    /// <summary>
    /// Forgets all samples and marks the popup closed.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the popup was open.
    /// </returns>
    public bool Reset()
    {
        var wasOpen = IsOpen;
        IsOpen = false;
        _inside = 0;
        _outside = 0;
        return wasOpen;
    }

    /// <summary>
    /// Closes the popup because the user acted on the icon, and keeps it closed until the pointer has left the icon once,
    /// as it is still on it and the popup would otherwise come straight back.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the popup was open.
    /// </returns>
    public bool Dismiss()
    {
        var wasOpen = Reset();
        _suppressed = true;
        return wasOpen;
    }
}

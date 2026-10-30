namespace FluxTick.App.ViewModels;

/// <summary>
/// The tabs of the settings page, in the order the page lists them. The value of a tab is its index in the tab strip.
/// </summary>
public enum SettingsTab
{
    /// <summary>
    /// The general switches: language, start-up, window behaviour.
    /// </summary>
    General = 0,

    /// <summary>
    /// The look of the watchlist.
    /// </summary>
    Appearance = 1,

    /// <summary>
    /// The user-defined groups.
    /// </summary>
    Groups = 2,

    /// <summary>
    /// The instruments the tray list shows.
    /// </summary>
    Tray = 3,

    /// <summary>
    /// The price alerts.
    /// </summary>
    Alerts = 4,

    /// <summary>
    /// The data sources.
    /// </summary>
    Sources = 5,

    /// <summary>
    /// The product information, updates, and the settings and log folders.
    /// </summary>
    About = 6,
}

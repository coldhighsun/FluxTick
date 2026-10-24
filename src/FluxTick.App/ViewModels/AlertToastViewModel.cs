namespace FluxTick.App.ViewModels;

/// <summary>
/// The text of one alert notification.
/// </summary>
/// <param name="Title">
/// The title.
/// </param>
/// <param name="Body">
/// The text below the title.
/// </param>
public sealed record AlertToastViewModel(string Title, string Body);

using CommunityToolkit.Mvvm.ComponentModel;
using FluxTick.App.Localization;
using FluxTick.App.Services;

namespace FluxTick.App.ViewModels;

/// <summary>
/// The base of view models that build text in code: it calls <see cref="OnLanguageChanged"/> on the UI thread whenever
/// the interface language changes, and stops following when the view model is disposed.
/// </summary>
public abstract class LocalizedViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Hands the language change to the UI thread; kept so that it can be unsubscribed again.
    /// </summary>
    private readonly EventHandler _languageChanged;

    /// <summary>
    /// Whether <see cref="Dispose"/> already ran.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalizedViewModel"/> class.
    /// </summary>
    /// <param name="ui">
    /// The dispatcher of the UI thread.
    /// </param>
    protected LocalizedViewModel(IUiDispatcher ui)
    {
        Ui = ui;
        _languageChanged = (_, _) => ui.Post(OnLanguageChanged);
        Loc.Instance.Changed += _languageChanged;
    }

    /// <summary>
    /// Gets the dispatcher of the UI thread.
    /// </summary>
    protected IUiDispatcher Ui { get; }

    /// <summary>
    /// Stops following the interface language and detaches everything the view model subscribed to. Calling it again
    /// does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Loc.Instance.Changed -= _languageChanged;
        Detach();
    }

    /// <summary>
    /// Builds the texts that depend on the language again. Runs on the UI thread.
    /// </summary>
    protected abstract void OnLanguageChanged();

    /// <summary>
    /// Unsubscribes from the events of other objects, which would otherwise keep the view model alive and calling.
    /// </summary>
    protected virtual void Detach()
    {
    }
}

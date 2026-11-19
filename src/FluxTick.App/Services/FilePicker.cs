using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace FluxTick.App.Services;

/// <summary>
/// A kind of file the user can choose, by name and by the patterns of its file names.
/// </summary>
/// <param name="Name">
/// The name shown in the dialog, for example "JSON files".
/// </param>
/// <param name="Patterns">
/// The patterns of the file names, for example <c>*.json</c>.
/// </param>
public sealed record FilePickerFilter(string Name, IReadOnlyList<string> Patterns);

/// <summary>
/// Lets the user choose a file to read or to write and hands out its stream, so that the rest of the app does not depend
/// on windows or on where the file is.
/// </summary>
public interface IFilePicker
{
    /// <summary>
    /// Asks the user for a file to read.
    /// </summary>
    /// <param name="title">
    /// The title of the dialog.
    /// </param>
    /// <param name="filter">
    /// The kind of file that is offered.
    /// </param>
    /// <returns>
    /// The stream of the file, which the caller disposes, or <see langword="null"/> when the user cancelled.
    /// </returns>
    Task<Stream?> OpenReadAsync(string title, FilePickerFilter filter);

    /// <summary>
    /// Asks the user for a file to write.
    /// </summary>
    /// <param name="title">
    /// The title of the dialog.
    /// </param>
    /// <param name="suggestedName">
    /// The file name the dialog starts with.
    /// </param>
    /// <param name="defaultExtension">
    /// The extension that is added when the user types a name without one, without a dot.
    /// </param>
    /// <param name="filter">
    /// The kind of file that is offered.
    /// </param>
    /// <returns>
    /// The stream of the file, which the caller disposes, or <see langword="null"/> when the user cancelled. The file is
    /// written from the start; what it held before is gone.
    /// </returns>
    Task<Stream?> OpenWriteAsync(string title, string suggestedName, string defaultExtension, FilePickerFilter filter);
}

/// <summary>
/// An <see cref="IFilePicker"/> that shows the file dialog of the platform over the window the user is working in.
/// </summary>
public sealed class AvaloniaFilePicker : IFilePicker
{
    /// <inheritdoc />
    public async Task<Stream?> OpenReadAsync(string title, FilePickerFilter filter)
    {
        if (GetStorage() is not { } storage)
        {
            return null;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [ToFileType(filter)],
        });
        return files.Count == 0 ? null : await files[0].OpenReadAsync();
    }

    /// <inheritdoc />
    public async Task<Stream?> OpenWriteAsync(string title, string suggestedName, string defaultExtension, FilePickerFilter filter)
    {
        if (GetStorage() is not { } storage)
        {
            return null;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = defaultExtension,
            FileTypeChoices = [ToFileType(filter)],
            ShowOverwritePrompt = true,
        });
        return file is null ? null : await file.OpenWriteAsync();
    }

    /// <summary>
    /// Converts a filter to the type of file of the dialog.
    /// </summary>
    /// <param name="filter">
    /// The filter.
    /// </param>
    /// <returns>
    /// The type of file.
    /// </returns>
    private static FilePickerFileType ToFileType(FilePickerFilter filter) => new(filter.Name) { Patterns = [.. filter.Patterns] };

    /// <summary>
    /// Finds the file system of the window the user is working in: the one that is active, or else the main window.
    /// </summary>
    /// <returns>
    /// The storage provider, or <see langword="null"/> when no window can show a dialog.
    /// </returns>
    private static IStorageProvider? GetStorage()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return null;
        }

        var window = desktop.Windows.FirstOrDefault(w => w.IsActive && w.IsVisible) ?? desktop.MainWindow;
        return window is null ? null : TopLevel.GetTopLevel(window)?.StorageProvider;
    }
}

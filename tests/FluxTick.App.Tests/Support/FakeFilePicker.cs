using FluxTick.App.Services;

namespace FluxTick.App.Tests;

/// <summary>
/// An <see cref="IFilePicker"/> that never shows a dialog: it hands out what a test gave it and keeps what was written.
/// </summary>
public sealed class FakeFilePicker : IFilePicker
{
    /// <summary>
    /// Gets or sets the content of the file the user "chooses" to read, or <see langword="null"/> when the user cancels.
    /// </summary>
    public byte[]? ReadBytes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user cancels the dialog for a file to write.
    /// </summary>
    public bool CancelWrite { get; set; }

    /// <summary>
    /// Gets or sets an exception the dialogs throw, or <see langword="null"/> for none.
    /// </summary>
    public Exception? Failure { get; set; }

    /// <summary>
    /// Gets what was written to the file, once its stream was disposed, or <see langword="null"/> when nothing was.
    /// </summary>
    public byte[]? Written { get; private set; }

    /// <summary>
    /// Gets the file name the last dialog for a file to write started with.
    /// </summary>
    public string? SuggestedName { get; private set; }

    /// <summary>
    /// Gets how often the dialog for a file to read was shown.
    /// </summary>
    public int ReadDialogs { get; private set; }

    /// <inheritdoc />
    public Task<Stream?> OpenReadAsync(string title, FilePickerFilter filter)
    {
        ReadDialogs++;
        if (Failure is not null)
        {
            throw Failure;
        }

        return Task.FromResult<Stream?>(ReadBytes is null ? null : new MemoryStream(ReadBytes));
    }

    /// <inheritdoc />
    public Task<Stream?> OpenWriteAsync(string title, string suggestedName, string defaultExtension, FilePickerFilter filter)
    {
        SuggestedName = suggestedName;
        if (Failure is not null)
        {
            throw Failure;
        }

        return Task.FromResult<Stream?>(CancelWrite ? null : new CapturingStream(bytes => Written = bytes));
    }

    /// <summary>
    /// A stream that hands over what was written to it when it is disposed.
    /// </summary>
    /// <param name="onDisposed">
    /// Receives what was written.
    /// </param>
    private sealed class CapturingStream(Action<byte[]> onDisposed) : MemoryStream
    {
        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                onDisposed(ToArray());
            }

            base.Dispose(disposing);
        }
    }
}

using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxTick.App.Localization;
using FluxTick.App.Services;
using FluxTick.Core.Application;
using Microsoft.Extensions.Logging;

namespace FluxTick.App.ViewModels;

/// <summary>
/// Writes the watchlist, the groups, the holdings and the alerts to a file and reads them back from one. A file that is
/// read is shown as what it would change first, and is only merged in when the user agrees.
/// </summary>
public sealed partial class DataTransferViewModel : LocalizedViewModel
{
    /// <summary>
    /// The largest file that is read, in bytes. A watchlist is a few kilobytes; a file far beyond that is not one.
    /// </summary>
    private const int MaxFileBytes = 8 * 1024 * 1024;

    /// <summary>
    /// The settings that are written and merged into.
    /// </summary>
    private readonly SettingsManager _settings;

    /// <summary>
    /// The data sources this app has, which decide what an import can use.
    /// </summary>
    private readonly ProviderRegistry _registry;

    /// <summary>
    /// Asks the user for the files.
    /// </summary>
    private readonly IFilePicker _picker;

    /// <summary>
    /// The clock, for the date in the name of a file.
    /// </summary>
    private readonly TimeProvider _time;

    /// <summary>
    /// The logger, or <see langword="null"/> for none.
    /// </summary>
    private readonly ILogger? _logger;

    /// <summary>
    /// What was read and waits for the user to agree, or <see langword="null"/> when nothing waits.
    /// </summary>
    private WatchlistImport? _pending;

    /// <summary>
    /// What merging <see cref="_pending"/> would have done when it was read.
    /// </summary>
    private ImportSummary? _pendingSummary;

    /// <summary>
    /// The key of the text that tells the last outcome, or <see langword="null"/> for none.
    /// </summary>
    private string? _messageKey;

    /// <summary>
    /// The values that go into the text of <see cref="_messageKey"/>.
    /// </summary>
    private object[] _messageArgs = [];

    /// <summary>
    /// Whether a file is being written or read, which keeps a second click from starting another.
    /// </summary>
    private bool _busy;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataTransferViewModel"/> class.
    /// </summary>
    /// <param name="settings">
    /// The settings that are written and merged into.
    /// </param>
    /// <param name="registry">
    /// The data sources this app has.
    /// </param>
    /// <param name="ui">
    /// The dispatcher of the UI thread.
    /// </param>
    /// <param name="picker">
    /// Asks the user for the files.
    /// </param>
    /// <param name="time">
    /// The clock.
    /// </param>
    /// <param name="logger">
    /// The logger, or <see langword="null"/> for none.
    /// </param>
    public DataTransferViewModel(
        SettingsManager settings,
        ProviderRegistry registry,
        IUiDispatcher ui,
        IFilePicker picker,
        TimeProvider time,
        ILogger? logger = null)
        : base(ui)
    {
        _settings = settings;
        _registry = registry;
        _picker = picker;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Gets a value indicating whether the last outcome was a failure.
    /// </summary>
    [ObservableProperty]
    public partial bool MessageIsError { get; private set; }

    /// <summary>
    /// Gets the text of the last outcome in the language of the interface, or an empty text for none.
    /// </summary>
    public string Message => _messageKey is null ? string.Empty : Loc.Get(_messageKey, _messageArgs);

    /// <summary>
    /// Gets a value indicating whether there is an outcome to show.
    /// </summary>
    public bool HasMessage => _messageKey is not null;

    /// <summary>
    /// Gets a value indicating whether a file was read and waits for the user to agree to merge it in.
    /// </summary>
    public bool HasPreview => _pending is not null;

    /// <summary>
    /// Gets what merging the file would do, one line each, in the language of the interface.
    /// </summary>
    public string PreviewText => _pendingSummary is { } summary ? DescribePreview(summary) : string.Empty;

    /// <summary>
    /// Writes the watchlist, groups, holdings and alerts to a JSON file the user chooses.
    /// </summary>
    /// <returns>
    /// A task that completes when the file is written or the user cancelled.
    /// </returns>
    [RelayCommand]
    private Task ExportJsonAsync() => ExportAsync(json: true);

    /// <summary>
    /// Writes the watchlist and its holdings to a table file the user chooses.
    /// </summary>
    /// <returns>
    /// A task that completes when the file is written or the user cancelled.
    /// </returns>
    [RelayCommand]
    private Task ExportCsvAsync() => ExportAsync(json: false);

    /// <summary>
    /// Asks the user for a file, reads it and shows what merging it in would do.
    /// </summary>
    /// <returns>
    /// A task that completes when the outcome is shown or the user cancelled.
    /// </returns>
    [RelayCommand]
    private async Task ImportAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            ClearPreview();
            string text;
            var filter = new FilePickerFilter(Loc.Get("Transfer_FileKindAny"), ["*.json", "*.csv"]);
            try
            {
                await using var stream = await _picker.OpenReadAsync(Loc.Get("Transfer_ImportTitle"), filter);
                if (stream is null)
                {
                    return;
                }

                if (await ReadTextAsync(stream) is not { } read)
                {
                    SetMessage("Transfer_Error_Unreadable", isError: true);
                    return;
                }

                text = read;
            }
            catch (Exception ex) when (IsFileFailure(ex))
            {
                _logger?.LogWarning(ex, "Could not read the file to import");
                SetMessage("Transfer_Error_IO", isError: true);
                return;
            }

            // A table has no marker; a JSON file begins with its object.
            var parsed = text.TrimStart((char)0xFEFF, ' ', '\t', '\r', '\n').StartsWith('{')
                ? WatchlistTransfer.ParseJson(text)
                : WatchlistTransfer.ParseCsv(text);
            if (parsed.Import is not { } import)
            {
                SetMessage($"Transfer_Error_{parsed.Error}", isError: true);
                return;
            }

            var trial = _settings.Current.Clone();
            var summary = WatchlistTransfer.Merge(trial, import, ProviderIds());
            if (summary.ChangesNothing)
            {
                SetMessage("Transfer_NothingNew", isError: false);
                return;
            }

            _pending = import;
            _pendingSummary = summary;
            SetMessage(null, isError: false);
            RaisePreviewChanged();
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Merges the file that was read into the settings.
    /// </summary>
    /// <returns>
    /// A task that completes when the settings are saved.
    /// </returns>
    [RelayCommand]
    private async Task ConfirmImportAsync()
    {
        if (_pending is not { } import || _busy)
        {
            return;
        }

        _busy = true;
        try
        {
            ImportSummary? done = null;
            var providers = ProviderIds();
            try
            {
                await _settings.UpdateAsync(settings => done = WatchlistTransfer.Merge(settings, import, providers));
            }
            catch (Exception ex) when (IsFileFailure(ex))
            {
                _logger?.LogWarning(ex, "Could not save the imported watchlist");
                SetMessage("Transfer_Error_IO", isError: true);
                return;
            }

            ClearPreview();
            SetMessage("Transfer_Imported", isError: false, done?.Added ?? 0, done?.AlertsAdded ?? 0);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Forgets the file that was read without merging it in.
    /// </summary>
    [RelayCommand]
    private void CancelImport() => ClearPreview();

    /// <inheritdoc />
    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(PreviewText));
    }

    /// <summary>
    /// Writes the settings to a file the user chooses.
    /// </summary>
    /// <param name="json">
    /// <see langword="true"/> for the JSON file with everything, <see langword="false"/> for the table of instruments.
    /// </param>
    /// <returns>
    /// A task that completes when the file is written or the user cancelled.
    /// </returns>
    private async Task ExportAsync(bool json)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            ClearPreview();
            var extension = json ? "json" : "csv";
            var name = $"fluxtick-watchlist-{_time.GetLocalNow():yyyyMMdd}.{extension}";
            var filter = new FilePickerFilter(Loc.Get(json ? "Transfer_FileKindJson" : "Transfer_FileKindCsv"), [$"*.{extension}"]);
            try
            {
                await using var stream = await _picker.OpenWriteAsync(Loc.Get("Transfer_ExportTitle"), name, extension, filter);
                if (stream is null)
                {
                    return;
                }

                var settings = _settings.Current;
                var text = json ? WatchlistTransfer.ExportJson(settings) : WatchlistTransfer.ExportCsv(settings);
                if (stream.CanSeek)
                {
                    stream.SetLength(0);
                }

                // A table gets a byte order mark, which is how spreadsheets tell UTF-8 from the local code page.
                await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: !json), leaveOpen: true);
                await writer.WriteAsync(text);
                await writer.FlushAsync();
                SetMessage("Transfer_Exported", isError: false, settings.Watchlist.Count);
            }
            catch (Exception ex) when (IsFileFailure(ex))
            {
                _logger?.LogWarning(ex, "Could not write the export file");
                SetMessage("Transfer_Error_IO", isError: true);
            }
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Reads all the text of a stream, unless it is larger than a watchlist file can be.
    /// </summary>
    /// <param name="stream">
    /// The stream.
    /// </param>
    /// <returns>
    /// The text, or <see langword="null"/> when the stream is too large.
    /// </returns>
    private static async Task<string?> ReadTextAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk)) > 0)
        {
            if (buffer.Length + read > MaxFileBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;
        using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync();
    }

    /// <summary>
    /// Determines whether an exception is a failure of the file or of the dialog that gets it, which the user is told
    /// about, rather than a defect.
    /// </summary>
    /// <param name="ex">
    /// The exception.
    /// </param>
    /// <returns>
    /// <see langword="true"/> for a failure to open, read or write a file.
    /// </returns>
    private static bool IsFileFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException;

    /// <summary>
    /// Lists the ids of the data sources this app has.
    /// </summary>
    /// <returns>
    /// The ids.
    /// </returns>
    private List<string> ProviderIds() => [.. _registry.Descriptors.Select(d => d.Id)];

    /// <summary>
    /// Sets the text that tells the last outcome.
    /// </summary>
    /// <param name="key">
    /// The key of the text, or <see langword="null"/> for none.
    /// </param>
    /// <param name="isError">
    /// Whether the outcome is a failure.
    /// </param>
    /// <param name="args">
    /// The values that go into the text.
    /// </param>
    private void SetMessage(string? key, bool isError, params object[] args)
    {
        _messageKey = key;
        _messageArgs = args;
        MessageIsError = isError;
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(HasMessage));
    }

    /// <summary>
    /// Forgets the file that was read.
    /// </summary>
    private void ClearPreview()
    {
        if (_pending is null)
        {
            return;
        }

        _pending = null;
        _pendingSummary = null;
        RaisePreviewChanged();
    }

    /// <summary>
    /// Announces that the preview changed.
    /// </summary>
    private void RaisePreviewChanged()
    {
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(PreviewText));
    }

    /// <summary>
    /// Describes what merging a file would do, leaving out what is zero.
    /// </summary>
    /// <param name="summary">
    /// What merging would do.
    /// </param>
    /// <returns>
    /// One line for each thing that is not zero.
    /// </returns>
    private static string DescribePreview(ImportSummary summary)
    {
        List<string> lines = [];
        (string Key, int Count)[] parts =
        [
            ("Transfer_PreviewAdded", summary.Added),
            ("Transfer_PreviewGroups", summary.GroupsAdded),
            ("Transfer_PreviewHoldings", summary.HoldingsFilled),
            ("Transfer_PreviewAlerts", summary.AlertsAdded),
            ("Transfer_PreviewSkipped", summary.Skipped),
            ("Transfer_PreviewAlertsSkipped", summary.AlertsSkipped),
            ("Transfer_PreviewUnknown", summary.UnknownProvider),
            ("Transfer_PreviewInvalid", summary.Invalid),
        ];
        foreach (var (key, count) in parts)
        {
            if (count > 0)
            {
                lines.Add(Loc.Get(key, count));
            }
        }

        return string.Join('\n', lines);
    }
}

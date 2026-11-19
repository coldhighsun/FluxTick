using System.Globalization;
using System.Text;
using System.Text.Json;
using FluxTick.Abstractions;

namespace FluxTick.Core.Application;

/// <summary>
/// Why a file could not be imported.
/// </summary>
public enum TransferError
{
    /// <summary>
    /// The text is not in the format it claims to be, or is empty.
    /// </summary>
    Unreadable,

    /// <summary>
    /// The file is JSON, but not a file made by this app.
    /// </summary>
    WrongFormat,

    /// <summary>
    /// The file was made by a newer version of the app, which this one cannot read.
    /// </summary>
    UnsupportedVersion,

    /// <summary>
    /// A table is missing a column it needs.
    /// </summary>
    MissingColumns,
}

/// <summary>
/// An instrument read from a file, with the name of the group it was in.
/// </summary>
/// <param name="Entry">
/// The instrument, with its holding if it had one.
/// </param>
/// <param name="GroupName">
/// The name of the group, or <see langword="null"/> when the file does not say.
/// </param>
public sealed record ImportedEntry(WatchlistEntry Entry, string? GroupName);

/// <summary>
/// What was read from a file, before it is merged into the settings.
/// </summary>
public sealed class WatchlistImport
{
    /// <summary>
    /// Gets the instruments of the file.
    /// </summary>
    public List<ImportedEntry> Entries { get; } = [];

    /// <summary>
    /// Gets the alert rules of the file.
    /// </summary>
    public List<AlertRule> Alerts { get; } = [];

    /// <summary>
    /// Gets or sets how many rows or items of the file could not be used and were left out.
    /// </summary>
    public int Invalid { get; set; }
}

/// <summary>
/// The outcome of reading a file.
/// </summary>
/// <param name="Import">
/// What was read, or <see langword="null"/> when the file cannot be imported.
/// </param>
/// <param name="Error">
/// Why the file cannot be imported, or <see langword="null"/> when it can.
/// </param>
public sealed record ImportParseResult(WatchlistImport? Import, TransferError? Error)
{
    /// <summary>
    /// Creates the outcome of a file that was read.
    /// </summary>
    /// <param name="import">
    /// What was read.
    /// </param>
    /// <returns>
    /// The outcome.
    /// </returns>
    public static ImportParseResult Success(WatchlistImport import) => new(import, null);

    /// <summary>
    /// Creates the outcome of a file that cannot be imported.
    /// </summary>
    /// <param name="error">
    /// Why.
    /// </param>
    /// <returns>
    /// The outcome.
    /// </returns>
    public static ImportParseResult Failure(TransferError error) => new(null, error);
}

/// <summary>
/// What merging a file into the settings added and what it left out.
/// </summary>
/// <param name="Added">
/// How many instruments were added.
/// </param>
/// <param name="Skipped">
/// How many instruments were left as they are, because the watchlist has them already or the file has them twice.
/// </param>
/// <param name="HoldingsFilled">
/// How many instruments that the watchlist has already got a holding from the file, because they had none.
/// </param>
/// <param name="UnknownProvider">
/// How many instruments were left out because their data source is not one of this app.
/// </param>
/// <param name="Invalid">
/// How many rows or items of the file could not be used.
/// </param>
/// <param name="GroupsAdded">
/// How many groups were created for the added instruments.
/// </param>
/// <param name="AlertsAdded">
/// How many alert rules were added.
/// </param>
/// <param name="AlertsSkipped">
/// How many alert rules were left out, because the instrument already has an alert with the condition or is not on the
/// watchlist.
/// </param>
public sealed record ImportSummary(
    int Added,
    int Skipped,
    int HoldingsFilled,
    int UnknownProvider,
    int Invalid,
    int GroupsAdded,
    int AlertsAdded,
    int AlertsSkipped)
{
    /// <summary>
    /// Gets a value indicating whether importing would change nothing.
    /// </summary>
    public bool ChangesNothing => Added == 0 && HoldingsFilled == 0 && AlertsAdded == 0;
}

/// <summary>
/// Writes the watchlist, the groups, the holdings and the alerts to a file and reads them back, so that they can be kept
/// or moved to another computer. The data sources with their options, which hold the API keys, and the appearance and
/// window settings are not part of a file.
/// </summary>
public static class WatchlistTransfer
{
    /// <summary>
    /// The value of the <c>format</c> field that marks a JSON file as one made by this app.
    /// </summary>
    public const string FormatId = "FluxTick.Watchlist";

    /// <summary>
    /// The version of the JSON file this app writes and the newest it can read.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// The columns of a table file, in the order they are written. <c>provider</c>, <c>symbol</c> and <c>class</c> are
    /// required when a table is read.
    /// </summary>
    public static readonly IReadOnlyList<string> CsvColumns = ["provider", "symbol", "name", "class", "group", "cost", "quantity", "currency"];

    /// <summary>
    /// Writes the watchlist, the groups, the holdings and the alerts as JSON.
    /// </summary>
    /// <param name="settings">
    /// The settings to take them from.
    /// </param>
    /// <returns>
    /// The JSON text.
    /// </returns>
    public static string ExportJson(AppSettings settings)
    {
        var file = new TransferFile
        {
            Format = FormatId,
            Version = CurrentVersion,
            Groups = [.. settings.Groups],
            Watchlist = [.. settings.Watchlist],
            Alerts = [.. settings.Alerts],
        };
        return JsonSerializer.Serialize(file, AppSettings.JsonOptions);
    }

    /// <summary>
    /// Writes the watchlist and its holdings as a table. Numbers are written without a thousands separator and with a
    /// point, whatever the language of the computer.
    /// </summary>
    /// <param name="settings">
    /// The settings to take the instruments from.
    /// </param>
    /// <returns>
    /// The text, with a header line.
    /// </returns>
    public static string ExportCsv(AppSettings settings)
    {
        var groups = settings.Groups.ToDictionary(g => g.Id, g => g.Name);
        var builder = new StringBuilder();
        builder.Append(string.Join(',', CsvColumns)).Append("\r\n");
        foreach (var entry in settings.Watchlist)
        {
            var group = entry.GroupId is { } id && groups.TryGetValue(id, out var name) ? name : string.Empty;
            string[] fields =
            [
                entry.ProviderId,
                entry.Symbol,
                entry.DisplayName,
                entry.AssetClass.ToString(),
                group,
                entry.HasHolding ? Number(entry.CostPrice) : string.Empty,
                entry.HasHolding ? Number(entry.Quantity) : string.Empty,
                entry.HasHolding ? entry.HoldingCurrency ?? string.Empty : string.Empty,
            ];
            builder.Append(string.Join(',', fields.Select(Quote))).Append("\r\n");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reads a JSON file made by <see cref="ExportJson"/>. Items that cannot be used are left out and counted.
    /// </summary>
    /// <param name="text">
    /// The JSON text.
    /// </param>
    /// <returns>
    /// What was read, or why the text cannot be imported.
    /// </returns>
    public static ImportParseResult ParseJson(string text)
    {
        TransferFile? file;
        try
        {
            file = JsonSerializer.Deserialize<TransferFile>(text, AppSettings.JsonOptions);
        }
        catch (JsonException)
        {
            return ImportParseResult.Failure(TransferError.Unreadable);
        }

        if (file is null)
        {
            return ImportParseResult.Failure(TransferError.Unreadable);
        }

        if (file.Format != FormatId)
        {
            return ImportParseResult.Failure(TransferError.WrongFormat);
        }

        if (file.Version is not (>= 1 and <= CurrentVersion))
        {
            return ImportParseResult.Failure(TransferError.UnsupportedVersion);
        }

        var import = new WatchlistImport();
        var groups = new Dictionary<string, string>();
        foreach (var group in file.Groups ?? [])
        {
            if (group is not null && WatchGroup.TryNormalizeName(group.Name, out var name))
            {
                groups[group.Id] = name;
            }
        }

        foreach (var entry in file.Watchlist ?? [])
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.ProviderId) || string.IsNullOrWhiteSpace(entry.Symbol))
            {
                import.Invalid++;
                continue;
            }

            var group = entry.GroupId is { } id && groups.TryGetValue(id, out var name) ? name : null;
            import.Entries.Add(new ImportedEntry(entry, group));
        }

        foreach (var rule in file.Alerts ?? [])
        {
            if (rule is null || string.IsNullOrWhiteSpace(rule.ProviderId) || string.IsNullOrWhiteSpace(rule.Symbol) || !AlertRule.IsValidThreshold(rule.Threshold))
            {
                import.Invalid++;
                continue;
            }

            import.Alerts.Add(rule);
        }

        return ImportParseResult.Success(import);
    }

    /// <summary>
    /// Reads a table file. The first line names the columns, in any order and in any case; a comma or a semicolon
    /// separates them, as spreadsheets write either one. Rows that cannot be used are left out and counted.
    /// </summary>
    /// <param name="text">
    /// The text of the table.
    /// </param>
    /// <returns>
    /// What was read, or why the text cannot be imported.
    /// </returns>
    public static ImportParseResult ParseCsv(string text)
    {
        text = text.TrimStart((char)0xFEFF);
        if (string.IsNullOrWhiteSpace(text))
        {
            return ImportParseResult.Failure(TransferError.Unreadable);
        }

        var lineEnd = text.IndexOf('\n');
        var firstLine = lineEnd < 0 ? text : text[..lineEnd];
        var delimiter = firstLine.Count(';') > firstLine.Count(',') ? ';' : ',';
        var rows = ReadRows(text, delimiter);
        if (rows.Count == 0)
        {
            return ImportParseResult.Failure(TransferError.Unreadable);
        }

        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Column(string name) => header.IndexOf(name);
        var provider = Column("provider");
        var symbol = Column("symbol");
        var assetClass = Column("class");
        if (provider < 0 || symbol < 0 || assetClass < 0)
        {
            return ImportParseResult.Failure(TransferError.MissingColumns);
        }

        var name = Column("name");
        var group = Column("group");
        var cost = Column("cost");
        var quantity = Column("quantity");
        var currency = Column("currency");
        var import = new WatchlistImport();
        foreach (var row in rows.Skip(1))
        {
            string Field(int column) => column >= 0 && column < row.Count ? row[column].Trim() : string.Empty;
            if (!Enum.TryParse<AssetClass>(Field(assetClass), ignoreCase: true, out var parsedClass)
                || !Enum.IsDefined(parsedClass)
                || Field(provider).Length == 0
                || Field(symbol).Length == 0
                || !TryNumber(Field(cost), out var costPrice)
                || !TryNumber(Field(quantity), out var units))
            {
                import.Invalid++;
                continue;
            }

            var entry = new WatchlistEntry
            {
                ProviderId = Field(provider),
                Symbol = Field(symbol),
                DisplayName = Field(name).Length > 0 ? Field(name) : Field(symbol),
                AssetClass = parsedClass,
                CostPrice = costPrice,
                Quantity = units,
                HoldingCurrency = Field(currency).Length > 0 ? Field(currency) : null,
            };
            import.Entries.Add(new ImportedEntry(entry, Field(group).Length > 0 ? Field(group) : null));
        }

        return ImportParseResult.Success(import);
    }

    /// <summary>
    /// Merges what was read from a file into the settings. Nothing the settings have is replaced or removed: an
    /// instrument that is on the watchlist already stays as it is, apart from a holding it gets when it has none, and
    /// an alert rule is added only when the instrument has no alert with the condition yet. The settings are normalized
    /// afterwards.
    /// </summary>
    /// <param name="target">
    /// The settings to merge into.
    /// </param>
    /// <param name="import">
    /// What was read from the file.
    /// </param>
    /// <param name="knownProviderIds">
    /// The ids of the data sources of this app. An instrument of another data source cannot be shown, so it is left out.
    /// </param>
    /// <returns>
    /// What was added and what was left out.
    /// </returns>
    public static ImportSummary Merge(AppSettings target, WatchlistImport import, IEnumerable<string> knownProviderIds)
    {
        var providers = knownProviderIds.ToDictionary(id => id, id => id, StringComparer.OrdinalIgnoreCase);
        int added = 0, skipped = 0, filled = 0, unknown = 0, groupsAdded = 0, alertsAdded = 0, alertsSkipped = 0;
        var seen = new HashSet<(string, string)>();
        foreach (var (entry, groupName) in import.Entries)
        {
            if (!providers.TryGetValue(entry.ProviderId, out var providerId))
            {
                unknown++;
                continue;
            }

            if (!seen.Add((providerId, entry.Symbol)))
            {
                skipped++;
                continue;
            }

            var existing = target.Watchlist.Find(e => e.ProviderId == providerId && e.Symbol == entry.Symbol);
            if (existing is not null)
            {
                if (!existing.HasHolding && entry.HasHolding)
                {
                    existing.CopyHoldingFrom(entry);
                    filled++;
                }
                else
                {
                    skipped++;
                }

                continue;
            }

            string? groupId = null;
            if (groupName is not null && WatchGroup.TryNormalizeName(groupName, out var name))
            {
                var group = target.Groups.Find(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
                if (group is null)
                {
                    group = new WatchGroup { Name = name };
                    target.Groups.Add(group);
                    groupsAdded++;
                }

                groupId = group.Id;
            }

            target.Watchlist.Add(new WatchlistEntry
            {
                ProviderId = providerId,
                Symbol = entry.Symbol,
                DisplayName = entry.DisplayName,
                AssetClass = entry.AssetClass,
                GroupId = groupId,
                CostPrice = entry.CostPrice,
                Quantity = entry.Quantity,
                HoldingCurrency = entry.HoldingCurrency,
            });
            added++;
        }

        foreach (var rule in import.Alerts)
        {
            if (!providers.TryGetValue(rule.ProviderId, out var providerId)
                || !target.Watchlist.Exists(e => e.ProviderId == providerId && e.Symbol == rule.Symbol)
                || target.Alerts.Exists(a => a.ProviderId == providerId && a.Symbol == rule.Symbol && a.Condition == rule.Condition))
            {
                alertsSkipped++;
                continue;
            }

            var copy = rule.Clone();
            copy.Id = Identifier.New();
            copy.ProviderId = providerId;
            target.Alerts.Add(copy);
            alertsAdded++;
        }

        target.Normalize();
        return new ImportSummary(added, skipped, filled, unknown, import.Invalid, groupsAdded, alertsAdded, alertsSkipped);
    }

    /// <summary>
    /// Writes a number the same way in every language.
    /// </summary>
    /// <param name="value">
    /// The number.
    /// </param>
    /// <returns>
    /// The text, or an empty text for no number.
    /// </returns>
    private static string Number(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>
    /// Reads a number of a table, which is either empty or written with a point.
    /// </summary>
    /// <param name="text">
    /// The text of the field.
    /// </param>
    /// <param name="value">
    /// The number, or <see langword="null"/> for an empty field.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the field is empty or a number.
    /// </returns>
    private static bool TryNumber(string text, out decimal? value)
    {
        value = null;
        if (text.Length == 0)
        {
            return true;
        }

        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        value = number;
        return true;
    }

    /// <summary>
    /// Puts a field in quotes when it holds the separator, a quote or a line break, and doubles the quotes in it.
    /// </summary>
    /// <param name="field">
    /// The field.
    /// </param>
    /// <returns>
    /// The field as it is written to a table.
    /// </returns>
    private static string Quote(string field) =>
        field.AsSpan().IndexOfAny(",;\"\r\n") >= 0 ? $"\"{field.Replace("\"", "\"\"")}\"" : field;

    /// <summary>
    /// Splits the text of a table into rows of fields. A field in quotes may hold the separator, line breaks and doubled
    /// quotes. Rows without any text are left out.
    /// </summary>
    /// <param name="text">
    /// The text.
    /// </param>
    /// <param name="delimiter">
    /// The separator of the fields.
    /// </param>
    /// <returns>
    /// The rows, which is none when the text has no field with anything in it.
    /// </returns>
    private static List<List<string>> ReadRows(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            if (c == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (c == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                EndRow();
            }
            else
            {
                field.Append(c);
            }
        }

        EndRow();
        return rows;

        void EndRow()
        {
            row.Add(field.ToString());
            field.Clear();
            if (row.Exists(f => f.Trim().Length > 0))
            {
                rows.Add(row);
            }

            row = [];
        }
    }

    /// <summary>
    /// The JSON file as it is written and read. The lists may hold nulls when read, which are left out.
    /// </summary>
    internal sealed class TransferFile
    {
        /// <summary>
        /// Gets or sets the marker that tells the file is one of this app.
        /// </summary>
        public string? Format { get; set; }

        /// <summary>
        /// Gets or sets the version of the file.
        /// </summary>
        public int? Version { get; set; }

        /// <summary>
        /// Gets or sets the groups.
        /// </summary>
        public List<WatchGroup?>? Groups { get; set; }

        /// <summary>
        /// Gets or sets the instruments with their holdings.
        /// </summary>
        public List<WatchlistEntry?>? Watchlist { get; set; }

        /// <summary>
        /// Gets or sets the alert rules.
        /// </summary>
        public List<AlertRule?>? Alerts { get; set; }
    }
}

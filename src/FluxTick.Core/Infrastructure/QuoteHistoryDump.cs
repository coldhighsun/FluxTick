using System.Globalization;
using FluxTick.Abstractions;

namespace FluxTick.Core.Infrastructure;

/// <summary>
/// Turns a price history file of <see cref="FileQuoteHistoryStore"/> into text, so that it can be looked at outside
/// the application.
/// </summary>
public static class QuoteHistoryDump
{
    /// <summary>
    /// The first line of the CSV text.
    /// </summary>
    public const string CsvHeader = "time_utc,price";

    /// <summary>
    /// Gets how many whole records a history file of a size holds.
    /// </summary>
    /// <param name="fileLength">
    /// The size of the file in bytes.
    /// </param>
    /// <returns>
    /// The number of records.
    /// </returns>
    public static long RecordCountOf(long fileLength) => fileLength / FileQuoteHistoryStore.RecordSize;

    /// <summary>
    /// Reads the prices of a history file; records that cannot be decoded and a record cut short at the end are skipped.
    /// </summary>
    /// <param name="path">
    /// The path of a <c>q-{symbol}.bin</c> file.
    /// </param>
    /// <returns>
    /// The prices in file order.
    /// </returns>
    public static IReadOnlyList<PricePoint> Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var points = new List<PricePoint>(bytes.Length / FileQuoteHistoryStore.RecordSize);
        for (var offset = 0; offset + FileQuoteHistoryStore.RecordSize <= bytes.Length; offset += FileQuoteHistoryStore.RecordSize)
        {
            if (FileQuoteHistoryStore.TryDecode(bytes.AsSpan(offset, FileQuoteHistoryStore.RecordSize), out var point))
            {
                points.Add(point);
            }
        }

        return points;
    }

    /// <summary>
    /// Writes prices as CSV: a header, then one line per price with an ISO 8601 UTC time and the price in the invariant
    /// culture.
    /// </summary>
    /// <param name="points">
    /// The prices.
    /// </param>
    /// <param name="writer">
    /// Where the text goes.
    /// </param>
    public static void WriteCsv(IEnumerable<PricePoint> points, TextWriter writer)
    {
        writer.WriteLine(CsvHeader);
        foreach (var point in points)
        {
            var time = point.Timestamp.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{time},{point.Price}"));
        }
    }
}

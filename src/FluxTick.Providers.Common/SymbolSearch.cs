using FluxTick.Abstractions;

namespace FluxTick.Providers.Common;

/// <summary>
/// An instrument an exchange lists.
/// </summary>
/// <param name="Symbol">
/// The symbol in the form the exchange's streaming API expects.
/// </param>
/// <param name="DisplayName">
/// A readable pair name such as <c>BTC/USDT</c>.
/// </param>
public readonly record struct ListedSymbol(string Symbol, string DisplayName);

/// <summary>
/// A listing of crypto pairs prepared for searching. The normalized forms that matching and ranking compare are
/// computed once here, so a search does not rebuild them for every pair on every keystroke.
/// </summary>
public sealed class SymbolIndex
{
    /// <summary>
    /// The prepared pairs.
    /// </summary>
    private readonly Entry[] _entries;

    /// <summary>
    /// Initializes a new instance of the <see cref="SymbolIndex"/> class.
    /// </summary>
    /// <param name="listed">
    /// The exchange's listing.
    /// </param>
    public SymbolIndex(IEnumerable<ListedSymbol> listed)
    {
        ArgumentNullException.ThrowIfNull(listed);

        _entries = listed.Select(Entry.Create).ToArray();
    }

    /// <summary>
    /// Finds the best matches for a normalized query.
    /// </summary>
    /// <param name="needle">
    /// The normalized query; not empty.
    /// </param>
    /// <param name="limit">
    /// The maximum number of results.
    /// </param>
    /// <returns>
    /// The matching pairs, best first.
    /// </returns>
    internal IEnumerable<ListedSymbol> Find(string needle, int limit) =>
        _entries
            .Where(e => e.Symbol.Contains(needle, StringComparison.Ordinal) || e.Name.Contains(needle, StringComparison.Ordinal))
            .OrderBy(e => e.MatchRank(needle))
            .ThenBy(e => e.QuoteRank)
            .ThenBy(e => e.Listed.Symbol.Length)
            .ThenBy(e => e.Listed.Symbol, StringComparer.Ordinal)
            .Take(limit)
            .Select(e => e.Listed);

    /// <summary>
    /// A pair together with the values that matching and ranking compare.
    /// </summary>
    /// <param name="Listed">
    /// The pair.
    /// </param>
    /// <param name="Symbol">
    /// The normalized symbol.
    /// </param>
    /// <param name="Name">
    /// The normalized display name.
    /// </param>
    /// <param name="BaseCurrency">
    /// The normalized base currency of the display name.
    /// </param>
    /// <param name="QuoteRank">
    /// How preferred the quote currency is; lower is better.
    /// </param>
    private readonly record struct Entry(ListedSymbol Listed, string Symbol, string Name, string BaseCurrency, int QuoteRank)
    {
        /// <summary>
        /// Prepares a pair for searching.
        /// </summary>
        /// <param name="listed">
        /// The pair.
        /// </param>
        /// <returns>
        /// The prepared entry.
        /// </returns>
        public static Entry Create(ListedSymbol listed)
        {
            var displayName = listed.DisplayName;
            var separator = displayName.IndexOf('/');
            var baseCurrency = SymbolSearch.Normalize(separator >= 0 ? displayName[..separator] : displayName);
            return new Entry(
                listed,
                SymbolSearch.Normalize(listed.Symbol),
                SymbolSearch.Normalize(displayName),
                baseCurrency,
                SymbolSearch.QuoteRank(displayName));
        }

        /// <summary>
        /// Ranks how well the pair matches: first the pairs whose base currency starts with the query, then pairs whose
        /// joined name does (so <c>doge</c> finds <c>DOGE/USD</c> before <c>DOG/EUR</c>), then every other match.
        /// </summary>
        /// <param name="needle">
        /// The normalized query.
        /// </param>
        /// <returns>
        /// The rank, lower is better.
        /// </returns>
        public int MatchRank(string needle)
        {
            if (BaseCurrency.StartsWith(needle, StringComparison.Ordinal))
            {
                return 0;
            }

            return Name.StartsWith(needle, StringComparison.Ordinal) ? 1 : 2;
        }
    }
}

/// <summary>
/// Ranks a listing of crypto pairs against a user's query.
/// </summary>
public static class SymbolSearch
{
    /// <summary>
    /// The quote currencies that are listed first among otherwise equal matches.
    /// </summary>
    private static readonly string[] PreferredQuotes = ["USDT", "USD", "USDC"];

    /// <summary>
    /// Finds the pairs matching a query. A match is a case-insensitive substring of the symbol or the display name,
    /// ignoring <c>/</c> and <c>-</c> separators; prefix matches and dollar-quoted pairs come first. Build the
    /// <see cref="SymbolIndex"/> once per downloaded listing and search it for every query.
    /// </summary>
    /// <param name="index">
    /// The prepared listing.
    /// </param>
    /// <param name="query">
    /// The text the user typed.
    /// </param>
    /// <param name="providerId">
    /// The provider id to stamp on the results.
    /// </param>
    /// <param name="limit">
    /// The maximum number of results.
    /// </param>
    /// <returns>
    /// The best matches, as crypto assets.
    /// </returns>
    public static IReadOnlyList<Asset> Search(SymbolIndex index, string query, string providerId, int limit = 20)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(providerId);

        var needle = Normalize(query);
        if (needle.Length == 0)
        {
            return [];
        }

        return index
            .Find(needle, limit)
            .Select(s => new Asset(providerId, s.Symbol, s.DisplayName, AssetClass.Crypto))
            .ToArray();
    }

    /// <summary>
    /// Turns an exchange id that joins base and quote with a dash, such as <c>BTC-USDT</c>, into the readable pair name
    /// <c>BTC/USDT</c>.
    /// </summary>
    /// <param name="id">
    /// The dash-separated id.
    /// </param>
    /// <returns>
    /// The readable pair name.
    /// </returns>
    public static string PairNameOfDashedId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        return id.Replace('-', '/');
    }

    /// <summary>
    /// Upper-cases text and drops pair separators so <c>btc usdt</c>, <c>BTC-USDT</c> and <c>BTC/USDT</c> compare equal.
    /// </summary>
    /// <param name="text">
    /// The text to normalize.
    /// </param>
    /// <returns>
    /// The normalized text.
    /// </returns>
    internal static string Normalize(string text) =>
        text.Trim().ToUpperInvariant().Replace("/", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Ranks a pair by its quote currency; dollar-quoted pairs sort first.
    /// </summary>
    /// <param name="displayName">
    /// The readable pair name.
    /// </param>
    /// <returns>
    /// The rank, lower is better.
    /// </returns>
    internal static int QuoteRank(string displayName)
    {
        var separator = displayName.LastIndexOf('/');
        var quote = separator >= 0 ? displayName[(separator + 1)..] : string.Empty;
        var index = Array.IndexOf(PreferredQuotes, quote.ToUpperInvariant());
        return index >= 0 ? index : PreferredQuotes.Length;
    }
}

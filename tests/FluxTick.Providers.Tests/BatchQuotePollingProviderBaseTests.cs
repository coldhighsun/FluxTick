using System.Collections.Concurrent;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for the shared batch quote polling, with a provider whose service is a delegate.
/// </summary>
public class BatchQuotePollingProviderBaseTests
{
    /// <summary>
    /// The descriptor of the test provider.
    /// </summary>
    private static readonly ProviderDescriptor TestDescriptor = new(
        "batch",
        "Batch",
        "A test source.",
        [AssetClass.Equity],
        ProviderUpdateMode.Polling);

    /// <summary>
    /// Verifies quotes are published under the symbol that was subscribed, however the service spells the code.
    /// </summary>
    [Fact]
    public async Task Poll_AnswerInOtherCase_IsPublishedUnderTheSubscribedSymbol()
    {
        await using var provider = new TestProvider(codes => [new ParsedQuote("ABC", 10m, 9m, null)]);
        var typed = new Asset("batch", "abc", "ABC", AssetClass.Equity);

        await provider.SetSubscriptionsAsync([typed]);
        var quote = (await TestWait.ReadQuotesAsync(provider, 1)).Single();

        quote.AssetId.Should().Be(typed.Id);
        quote.Price.Should().Be(10m);
        quote.Reference.Should().Be(9m);
    }

    /// <summary>
    /// Verifies instruments that share a code, here differing only in case, are requested once and all get the quote.
    /// </summary>
    [Fact]
    public async Task Poll_SubscriptionsSharingACode_AreRequestedOnceAndAllPublished()
    {
        await using var provider = new TestProvider(codes => [new ParsedQuote("abc", 10m, null, null)]);
        var first = new Asset("batch", "abc", "one", AssetClass.Equity);
        var second = new Asset("batch", "ABC", "two", AssetClass.Equity);

        await provider.SetSubscriptionsAsync([first, second]);
        var quotes = await TestWait.ReadQuotesAsync(provider, 2);

        quotes.Select(q => q.AssetId).Should().BeEquivalentTo([first.Id, second.Id]);
        provider.Requests.Should().ContainSingle().Which.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies a large watchlist is split into batches of at most 50 codes that together cover every code.
    /// </summary>
    [Fact]
    public async Task Poll_LargeWatchlist_IsSplitIntoBatchesOfAtMostFifty()
    {
        await using var provider = new TestProvider(codes => []);
        var assets = Enumerable.Range(0, 120).Select(i => new Asset("batch", $"c{i}", $"C{i}", AssetClass.Equity)).ToArray();

        await provider.SetSubscriptionsAsync(assets);
        await TestWait.EventuallyAsync(() => provider.Requests.Count == 3);

        provider.Requests.Should().OnlyContain(batch => batch.Length <= 50);
        provider.Requests.SelectMany(batch => batch).Should().BeEquivalentTo(assets.Select(a => a.Symbol));
    }

    /// <summary>
    /// Verifies a code the service did not answer is reported while the others keep updating.
    /// </summary>
    [Fact]
    public async Task Poll_CodeWithoutAnswer_IsReportedButOthersContinue()
    {
        await using var provider = new TestProvider(codes => [new ParsedQuote("a", 1m, null, null)]);

        await provider.SetSubscriptionsAsync([new Asset("batch", "a", "A", AssetClass.Equity), new Asset("batch", "b", "B", AssetClass.Equity)]);
        await TestWait.ReadQuotesAsync(provider, 1);

        await TestWait.EventuallyAsync(() => provider.Status.Message == "No data for b");
        provider.Status.State.Should().Be(ProviderConnectionState.Connected);
    }

    /// <summary>
    /// Verifies a failing request is a failed cycle that carries the error to the status.
    /// </summary>
    [Fact]
    public async Task Poll_FailingRequest_ReportsReconnectingWithTheError()
    {
        await using var provider = new TestProvider(codes => throw new HttpRequestException("rejected"));

        await provider.SetSubscriptionsAsync([new Asset("batch", "a", "A", AssetClass.Equity)]);

        await TestWait.EventuallyAsync(() => provider.Status is { State: ProviderConnectionState.Reconnecting, Message: "rejected" });
    }

    /// <summary>
    /// A provider that answers from a delegate and records the codes of every request.
    /// </summary>
    /// <param name="answer">
    /// Produces the answer for the codes of one request.
    /// </param>
    private sealed class TestProvider(Func<string[], IReadOnlyList<ParsedQuote>> answer)
        : BatchQuotePollingProviderBase(TestDescriptor, TimeSpan.FromSeconds(5), new FakeTimeProvider(), NullLogger.Instance)
    {
        /// <summary>
        /// Gets the codes of the requests received so far.
        /// </summary>
        public ConcurrentQueue<string[]> Requests { get; } = new();

        /// <inheritdoc />
        protected override string ToRequestCode(string symbol) => symbol.ToLowerInvariant();

        /// <inheritdoc />
        protected override Task<IReadOnlyList<ParsedQuote>> RequestQuotesAsync(string[] codes, CancellationToken cancellationToken)
        {
            Requests.Enqueue(codes);

            return Task.FromResult(answer(codes));
        }
    }
}

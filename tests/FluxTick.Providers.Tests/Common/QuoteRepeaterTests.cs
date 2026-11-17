using FluxTick.Abstractions;
using FluxTick.Providers.Common;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for the repeating of the last quote of a shut market.
/// </summary>
public class QuoteRepeaterTests
{
    /// <summary>
    /// The first test instrument.
    /// </summary>
    private static readonly Asset First = new("test", "A", "A", AssetClass.Equity);

    /// <summary>
    /// The second test instrument.
    /// </summary>
    private static readonly Asset Second = new("test", "B", "B", AssetClass.Equity);

    /// <summary>
    /// The time of the quotes.
    /// </summary>
    private static readonly DateTimeOffset Traded = new(2026, 10, 28, 7, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies a quote of an instrument that is no longer subscribed is not kept, so that it is not repeated if the
    /// instrument comes back.
    /// </summary>
    [Fact]
    public void Remember_InstrumentNoLongerSubscribed_KeepsNothing()
    {
        var repeater = new QuoteRepeater(_ => false);

        repeater.Remember(new Quote(First.Id, 1m, null, Traded));

        repeater.Split([First], _ => true, askUnpriced: true, out var shut).Should().Equal(First);
        shut.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies a quote forgotten with its instrument is not repeated any more.
    /// </summary>
    [Fact]
    public void Forget_RememberedQuote_IsNotRepeated()
    {
        var repeater = new QuoteRepeater(_ => true);
        repeater.Remember(new Quote(First.Id, 1m, null, Traded));

        repeater.Forget(First.Id);

        var published = new List<Quote>();
        repeater.Repeat([First], published.Add);
        published.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies a quote published while a repeat runs is not remembered, so that an older quote does not replace a newer one.
    /// </summary>
    [Fact]
    public void Remember_WhileRepeating_KeepsTheQuoteRemembered()
    {
        var repeater = new QuoteRepeater(_ => true);
        var newer = new Quote(First.Id, 2m, null, Traded);
        repeater.Remember(newer);

        repeater.Repeat([First], older =>
        {
            QuoteRepeater.IsRepeating.Should().BeTrue();
            repeater.Remember(older with { Price = 1m });
        });

        var published = new List<Quote>();
        repeater.Repeat([First], published.Add);
        published.Should().Equal(newer);
        QuoteRepeater.IsRepeating.Should().BeFalse();
    }

    /// <summary>
    /// Verifies only the instruments that are given are repeated, each with its own latest quote, and one without a quote is
    /// skipped.
    /// </summary>
    [Fact]
    public void Repeat_GivenInstruments_PublishesTheirLatestQuotes()
    {
        var repeater = new QuoteRepeater(_ => true);
        var quote = new Quote(First.Id, 1m, null, Traded);
        repeater.Remember(quote);
        repeater.Remember(new Quote(Second.Id, 2m, null, Traded));
        var third = new Asset("test", "C", "C", AssetClass.Equity);

        var published = new List<Quote>();
        repeater.Repeat([First, third], published.Add);

        published.Should().Equal(quote);
    }

    /// <summary>
    /// Verifies the market of every instrument is asked once, and that a shut instrument without a quote is asked for or
    /// not as requested.
    /// </summary>
    [Theory]
    [InlineData(true, new[] { "B" }, new[] { "A", "C" })]
    [InlineData(false, new[] { "B", "C" }, new[] { "A" })]
    public void Split_ShutInstruments_AreSeparatedByWhetherTheyHaveAQuote(bool askUnpriced, string[] shutSymbols, string[] openSymbols)
    {
        var repeater = new QuoteRepeater(_ => true);
        var third = new Asset("test", "C", "C", AssetClass.Equity);
        repeater.Remember(new Quote(Second.Id, 1m, null, Traded));
        var asked = new List<string>();

        var open = repeater.Split(
            [First, Second, third],
            a =>
            {
                asked.Add(a.Symbol);
                return a.Symbol != "A";
            },
            askUnpriced,
            out var shut);

        shut.Select(a => a.Symbol).Should().Equal(shutSymbols);
        open.Select(a => a.Symbol).Should().Equal(openSymbols);
        asked.Should().Equal("A", "B", "C");
    }
}

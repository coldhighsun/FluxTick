using FluxTick.Abstractions;
using FluxTick.Core.Tests.TestDoubles;

namespace FluxTick.Core.Tests;

/// <summary>
/// Tests for the value types and the provider base class in the Abstractions assembly.
/// </summary>
public class AbstractionsTests
{
    /// <summary>
    /// The identifier used by tests.
    /// </summary>
    private static readonly AssetId Btc = new("fake", "BTC");

    /// <summary>
    /// Verifies the absolute and relative change are derived from the reference price.
    /// </summary>
    [Fact]
    public void Change_WithReference_ReturnsDifferenceAndPercent()
    {
        var quote = new Quote(Btc, 110m, 100m, DateTimeOffset.UnixEpoch);

        quote.Change.Should().Be(10m);
        quote.ChangePercent.Should().Be(10m);
    }

    /// <summary>
    /// Verifies a missing or zero reference yields no change values instead of throwing.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void ChangePercent_WithoutUsableReference_ReturnsNull(int? reference)
    {
        var quote = new Quote(Btc, 110m, reference, DateTimeOffset.UnixEpoch);

        quote.ChangePercent.Should().BeNull();
    }

    /// <summary>
    /// Verifies invalid option values fall back to the declared default.
    /// </summary>
    [Fact]
    public void GetDuration_WithInvalidStoredValue_ReturnsDefault()
    {
        var factory = new FakeProviderFactory("fake", [AssetClass.Crypto]);
        var descriptor = factory.Descriptor with
        {
            Options = [new ProviderOptionDefinition("Poll", "Poll", ProviderOptionType.Duration, "00:00:05", Min: 2, Max: 60)],
        };

        var tooSmall = new ProviderOptions(descriptor, new Dictionary<string, string> { ["Poll"] = "00:00:01" });
        var valid = new ProviderOptions(descriptor, new Dictionary<string, string> { ["Poll"] = "00:00:10" });

        tooSmall.GetDuration("Poll").Should().Be(TimeSpan.FromSeconds(5));
        valid.GetDuration("Poll").Should().Be(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Verifies reading an undeclared option is a programming error.
    /// </summary>
    [Fact]
    public void GetString_WithUnknownKey_Throws()
    {
        var factory = new FakeProviderFactory("fake", [AssetClass.Crypto]);
        var options = new ProviderOptions(factory.Descriptor);

        var act = () => options.GetString("Nope");

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Verifies each option type validates its text form.
    /// </summary>
    [Theory]
    [InlineData(ProviderOptionType.Int, "12", true)]
    [InlineData(ProviderOptionType.Int, "x", false)]
    [InlineData(ProviderOptionType.Bool, "true", true)]
    [InlineData(ProviderOptionType.Bool, "yes", false)]
    [InlineData(ProviderOptionType.Duration, "00:00:05", true)]
    [InlineData(ProviderOptionType.Duration, "5", false)]
    [InlineData(ProviderOptionType.String, "anything", true)]
    public void Validate_ByType_AcceptsOnlyWellFormedValues(ProviderOptionType type, string value, bool valid)
    {
        var definition = new ProviderOptionDefinition("k", "k", type, "0");

        (definition.Validate(value) is null).Should().Be(valid);
    }

    /// <summary>
    /// Verifies a rejected value reports what is wrong with it, so the UI can word it in its own language.
    /// </summary>
    [Theory]
    [InlineData(ProviderOptionType.Int, "x", OptionValidationError.NotWholeNumber, null)]
    [InlineData(ProviderOptionType.Bool, "yes", OptionValidationError.NotBool, null)]
    [InlineData(ProviderOptionType.Duration, "5", OptionValidationError.NotDuration, null)]
    [InlineData(ProviderOptionType.Int, "1", OptionValidationError.BelowMinimum, 2.0)]
    [InlineData(ProviderOptionType.Int, "9", OptionValidationError.AboveMaximum, 5.0)]
    public void Check_InvalidValue_ReportsTheReason(ProviderOptionType type, string value, OptionValidationError error, double? limit)
    {
        var definition = new ProviderOptionDefinition("k", "k", type, "3", Min: 2, Max: 5);

        var result = definition.Check(value);

        result.Should().NotBeNull();
        result!.Value.Error.Should().Be(error);
        result.Value.Limit.Should().Be(limit);
        result.Value.Message.Should().Be(definition.Validate(value));
    }

    /// <summary>
    /// Verifies values that mean the same are written in one canonical form, and values that cannot be read stay as they are.
    /// </summary>
    [Theory]
    [InlineData(ProviderOptionType.Int, "05", "5")]
    [InlineData(ProviderOptionType.Int, " 5 ", "5")]
    [InlineData(ProviderOptionType.Int, "x", "x")]
    [InlineData(ProviderOptionType.Bool, "True", "true")]
    [InlineData(ProviderOptionType.Bool, "FALSE", "false")]
    [InlineData(ProviderOptionType.Bool, "yes", "yes")]
    [InlineData(ProviderOptionType.Duration, "00:00:05.0000000", "00:00:05")]
    [InlineData(ProviderOptionType.Duration, "00:00:00.5", "00:00:00.5000000")]
    [InlineData(ProviderOptionType.Duration, "5", "5")]
    [InlineData(ProviderOptionType.String, " keep as typed ", " keep as typed ")]
    [InlineData(ProviderOptionType.Secret, "Key", "Key")]
    public void Canonicalize_ByType_WritesEquivalentValuesTheSameWay(ProviderOptionType type, string value, string expected)
    {
        var definition = new ProviderOptionDefinition("k", "k", type, "0");

        definition.Canonicalize(value).Should().Be(expected);
    }

    /// <summary>
    /// Verifies stored durations are read and written in one form, and a bare number is not taken for days.
    /// </summary>
    [Theory]
    [InlineData("00:00:05", true, "00:00:05")]
    [InlineData("00:00:05.0000000", true, "00:00:05")]
    [InlineData("1.00:00:00", true, "1.00:00:00")]
    [InlineData("5", false, null)]
    [InlineData("", false, null)]
    [InlineData("soon", false, null)]
    [InlineData(null, false, null)]
    public void TryParseDuration_ReadsOnlyTheStoredForm(string? value, bool valid, string? formatted)
    {
        var ok = ProviderOptionDefinition.TryParseDuration(value, out var duration);

        ok.Should().Be(valid);
        if (valid)
        {
            ProviderOptionDefinition.FormatDuration(duration).Should().Be(formatted);
        }
    }

    /// <summary>
    /// Verifies a choice option only accepts listed values.
    /// </summary>
    [Fact]
    public void Validate_ChoiceOutsideList_ReturnsError()
    {
        var definition = new ProviderOptionDefinition("k", "k", ProviderOptionType.Choice, "a", Choices: ["a", "b"]);

        definition.Validate("b").Should().BeNull();
        definition.Validate("c").Should().NotBeNull();
    }

    /// <summary>
    /// Verifies subscriptions of other providers are ignored and duplicates collapse.
    /// </summary>
    [Fact]
    public async Task SetSubscriptionsAsync_WithForeignAndDuplicateAssets_KeepsOwnUniqueAssets()
    {
        var factory = new FakeProviderFactory("fake", [AssetClass.Crypto]);
        await using var provider = (FakeProvider)factory.Create(NewContext(factory));
        var mine = new Asset("fake", "BTC", "Bitcoin", AssetClass.Crypto);
        var foreign = new Asset("other", "ETH", "Ether", AssetClass.Crypto);

        await provider.SetSubscriptionsAsync([mine, mine, foreign]);

        provider.Subscribed.Should().ContainSingle().Which.Should().Be(mine);
    }

    /// <summary>
    /// Verifies quotes for instruments that are not subscribed are dropped.
    /// </summary>
    [Fact]
    public async Task Publish_ForUnsubscribedAsset_IsDropped()
    {
        var factory = new FakeProviderFactory("fake", [AssetClass.Crypto]);
        var provider = (FakeProvider)factory.Create(NewContext(factory));
        await provider.SetSubscriptionsAsync([new Asset("fake", "BTC", "Bitcoin", AssetClass.Crypto)]);

        provider.Emit(new Quote(new AssetId("fake", "ETH"), 1m, null, DateTimeOffset.UnixEpoch));
        provider.Emit(new Quote(Btc, 2m, null, DateTimeOffset.UnixEpoch));
        await provider.DisposeAsync();

        var received = new List<Quote>();
        await foreach (var quote in provider.ReadQuotesAsync())
        {
            received.Add(quote);
        }

        received.Should().ContainSingle().Which.Price.Should().Be(2m);
    }

    /// <summary>
    /// Verifies disposal ends the quote stream and further subscription changes are rejected.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_EndsStreamAndRejectsSubscriptions()
    {
        var factory = new FakeProviderFactory("fake", [AssetClass.Crypto]);
        var provider = (FakeProvider)factory.Create(NewContext(factory));
        var reader = Task.Run(async () =>
        {
            await foreach (var _ in provider.ReadQuotesAsync())
            {
            }
        });

        await provider.DisposeAsync();

        await reader.WaitAsync(TimeSpan.FromSeconds(5));
        provider.IsDisposed.Should().BeTrue();
        var act = () => provider.SetSubscriptionsAsync([]);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    /// <summary>
    /// Verifies status changes raise an event only when the status actually changes.
    /// </summary>
    [Fact]
    public async Task StatusChanged_RaisedOnlyOnRealChange()
    {
        var factory = new FakeProviderFactory("fake", [AssetClass.Crypto]);
        await using var provider = (FakeProvider)factory.Create(NewContext(factory));
        var raised = 0;
        provider.StatusChanged += (_, _) => raised++;

        provider.ChangeStatus(ProviderConnectionState.Connected);
        provider.ChangeStatus(ProviderConnectionState.Connected);
        provider.ChangeStatus(ProviderConnectionState.Failed, "boom");

        raised.Should().Be(2);
        provider.Status.Should().Be(new ProviderStatus(ProviderConnectionState.Failed, "boom"));
    }

    /// <summary>
    /// Creates a provider creation context with default options.
    /// </summary>
    /// <param name="factory">
    /// The factory whose descriptor supplies the options.
    /// </param>
    /// <returns>
    /// The context.
    /// </returns>
    private static ProviderCreationContext NewContext(FakeProviderFactory factory) => new(
        new ProviderOptions(factory.Descriptor),
        Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
        TimeProvider.System);
}

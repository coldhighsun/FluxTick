using FluxTick.Abstractions;
using FluxTick.App.Services;
using FluxTick.App.ViewModels;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for <see cref="AssetRowViewModel"/> and <see cref="OptionViewModel"/>.
/// </summary>
public class RowAndOptionTests
{
    /// <summary>
    /// The instrument used by the row tests.
    /// </summary>
    private static readonly Asset Btc = new("crypto", "BTC", "Bitcoin", AssetClass.Crypto);

    /// <summary>
    /// Builds a snapshot with a quote.
    /// </summary>
    /// <param name="price">
    /// The price.
    /// </param>
    /// <param name="reference">
    /// The reference price.
    /// </param>
    /// <param name="health">
    /// The data health.
    /// </param>
    /// <returns>
    /// The snapshot.
    /// </returns>
    private static AssetSnapshot Snapshot(decimal price, decimal reference, AssetHealth health = AssetHealth.Live) =>
        new(Btc, new Quote(Btc.Id, price, reference, DateTimeOffset.UnixEpoch), [reference, price], health);

    /// <summary>
    /// Verifies the row shows formatted values and direction.
    /// </summary>
    [Fact]
    public void Update_WithQuote_FormatsPriceChangeAndDirection()
    {
        var row = new AssetRowViewModel(Btc, "Crypto Co");

        row.Update(Snapshot(110m, 100m));

        row.PriceText.Should().Be("110.00");
        row.ChangeText.Should().Be("+10.00%");
        row.IsUp.Should().BeTrue();
        row.IsDown.Should().BeFalse();
        row.HasStatus.Should().BeFalse();
        row.Sparkline.Should().Equal(100m, 110m);
    }

    /// <summary>
    /// Verifies the figures of the large card: the reference price, the change in amount and the range of the chart.
    /// </summary>
    [Fact]
    public void Update_WithQuote_FormatsTheFiguresOfTheLargeCard()
    {
        var row = new AssetRowViewModel(Btc, "Crypto Co");
        var snapshot = new AssetSnapshot(Btc, new Quote(Btc.Id, 110m, 100m, DateTimeOffset.UnixEpoch), [95m, 120m, 110m], AssetHealth.Live);

        row.Update(snapshot);

        row.ReferenceText.Should().Be("100.00");
        row.ChangeAmountText.Should().Be("+10.00");
        row.LowText.Should().Be("95.00");
        row.HighText.Should().Be("120.00");
    }

    /// <summary>
    /// Verifies the figures of the large card show a placeholder where the data is missing.
    /// </summary>
    [Fact]
    public void Update_WithoutReferenceAndHistory_ShowsPlaceholders()
    {
        var row = new AssetRowViewModel(Btc, "Crypto Co");
        var snapshot = new AssetSnapshot(Btc, new Quote(Btc.Id, 110m, null, DateTimeOffset.UnixEpoch), [], AssetHealth.Live);

        row.Update(snapshot);

        row.ReferenceText.Should().Be(PriceFormatter.Unavailable);
        row.ChangeAmountText.Should().BeEmpty();
        row.LowText.Should().Be(PriceFormatter.Unavailable);
        row.HighText.Should().Be(PriceFormatter.Unavailable);
    }

    /// <summary>
    /// Verifies a small card moves what it does not show into the tooltip, and the usual card keeps its short tooltip.
    /// </summary>
    [Fact]
    public void Tooltip_SmallCard_TellsWhatTheCardLeavesOut()
    {
        var row = new AssetRowViewModel(Btc, "Crypto Co");
        row.Update(Snapshot(110m, 100m, AssetHealth.Stale));
        var usual = row.Tooltip;

        row.CardSize = CardSize.Small;
        row.AlertAbove = 150m;
        row.AlertBelow = 90m;

        usual.Should().NotContain("BTC").And.NotContain("150");
        row.Tooltip.Should().Contain("Bitcoin (BTC)")
            .And.Contain("Crypto Co")
            .And.Contain("+10.00%")
            .And.Contain("150.00")
            .And.Contain("90.00")
            .And.Contain(row.StatusText);

        row.CardSize = CardSize.Medium;
        row.Tooltip.Should().Be(usual);
    }

    /// <summary>
    /// Verifies the flash counter only advances when the price actually changes, with the right direction.
    /// </summary>
    [Fact]
    public void Update_FlashVersion_AdvancesOnlyOnPriceChange()
    {
        var row = new AssetRowViewModel(Btc, "p") { RollEnabled = true };

        row.Update(Snapshot(100m, 100m));
        row.RollVersion.Should().Be(0, "the first quote has nothing to compare with");

        row.Update(Snapshot(101m, 100m));
        row.RollVersion.Should().Be(1);
        row.RollUp.Should().BeTrue();

        row.Update(Snapshot(101m, 100m));
        row.RollVersion.Should().Be(1);

        row.Update(Snapshot(99m, 100m));
        row.RollVersion.Should().Be(2);
        row.RollUp.Should().BeFalse();
        row.IsDown.Should().BeTrue();
    }

    /// <summary>
    /// Verifies the price that rolls out is the one that was shown before, set when the version moves on, and that a
    /// change which does not show in the text because of the rounding does not roll.
    /// </summary>
    [Fact]
    public void Update_PriceChange_KeepsTheOldPriceForTheRoll()
    {
        var row = new AssetRowViewModel(Btc, "p") { RollEnabled = true };
        row.Update(Snapshot(100m, 100m));
        var seen = new List<(int Version, string Old, string Now)>();
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AssetRowViewModel.RollVersion))
            {
                seen.Add((row.RollVersion, row.PreviousPriceText, row.PriceText));
            }
        };

        row.Update(Snapshot(101m, 100m));
        row.Update(Snapshot(99m, 100m));
        row.Update(Snapshot(99.001m, 100m));

        seen.Should().Equal([(1, "100.00", "101.00"), (2, "101.00", "99.00")]);
    }

    /// <summary>
    /// Verifies a disabled flash keeps the counter still while the price still updates, and resumes once re-enabled.
    /// </summary>
    [Fact]
    public void Update_FlashDisabled_DoesNotAdvanceFlashVersion()
    {
        var row = new AssetRowViewModel(Btc, "p");
        row.RollEnabled.Should().BeFalse("the flash is off unless the setting turns it on");

        row.Update(Snapshot(100m, 100m));
        row.Update(Snapshot(101m, 100m));

        row.RollVersion.Should().Be(0);
        row.PriceText.Should().Be("101.00");

        row.RollEnabled = true;
        row.Update(Snapshot(101m, 100m));
        row.RollVersion.Should().Be(0, "the price did not change since the last quote");

        row.Update(Snapshot(102m, 100m));
        row.RollVersion.Should().Be(1);
        row.RollUp.Should().BeTrue();
    }

    /// <summary>
    /// Verifies data that is not coming in is dimmed, which the sparkline shows as stopped, and the latest price is
    /// kept for the alert lines.
    /// </summary>
    [Fact]
    public void Update_WithDataNotComingIn_DimsTheRowAndKeepsThePrice()
    {
        var row = new AssetRowViewModel(Btc, "p");

        row.Update(Snapshot(110m, 100m, AssetHealth.Reconnecting));

        row.IsStopped.Should().BeTrue();
        row.IsDimmed.Should().BeTrue();
        row.CurrentPrice.Should().Be(110m);

        row.Update(Snapshot(111m, 100m, AssetHealth.Live));

        row.IsStopped.Should().BeFalse();
        row.IsDimmed.Should().BeFalse();
        row.CurrentPrice.Should().Be(111m);
    }

    /// <summary>
    /// Verifies the row passes on when the sparkline prices were traded, and clears it when the snapshot has no times.
    /// </summary>
    [Fact]
    public void Update_WithTimeline_PassesItToTheSparkline()
    {
        var row = new AssetRowViewModel(Btc, "p");
        var asOf = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeline = new SparklineTimeline([asOf.AddMinutes(-2), asOf.AddMinutes(-1)], asOf, TimeSpan.FromMinutes(60));

        row.Update(Snapshot(110m, 100m) with { Timeline = timeline });

        row.SparklineTimeline.Should().BeSameAs(timeline);

        row.Update(Snapshot(111m, 100m));

        row.SparklineTimeline.Should().BeNull();
    }

    /// <summary>
    /// Verifies unhealthy data is dimmed and labelled.
    /// </summary>
    [Theory]
    [InlineData(AssetHealth.Stale, "Stale", true)]
    [InlineData(AssetHealth.Reconnecting, "Reconnecting", true)]
    [InlineData(AssetHealth.ProviderDisabled, "Source off", true)]
    [InlineData(AssetHealth.Live, "", false)]
    [InlineData(AssetHealth.Waiting, "", false)]
    public void Update_WithHealth_SetsStatusAndDimming(AssetHealth health, string status, bool dimmed)
    {
        var row = new AssetRowViewModel(Btc, "p");

        row.Update(new AssetSnapshot(Btc, null, [], health));

        row.StatusText.Should().Be(status);
        row.HasStatus.Should().Be(status.Length > 0);
        row.IsDimmed.Should().Be(dimmed);
    }

    /// <summary>
    /// Verifies a duration option is shown in seconds and committed in the stored format.
    /// </summary>
    [Fact]
    public void Option_Duration_ShowsSecondsAndCommitsStoredFormat()
    {
        var definition = new ProviderOptionDefinition("Poll", "Poll", ProviderOptionType.Duration, "00:00:05", Min: 2, Max: 60);
        var commits = 0;
        var option = new OptionViewModel(definition, "00:00:10", () => commits++);

        option.Text.Should().Be("10");
        commits.Should().Be(0, "loading must not commit");

        option.Text = "20";

        commits.Should().Be(1);
        option.StoredValue.Should().Be("00:00:20");
        option.Error.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies invalid input is rejected with an error and not committed.
    /// </summary>
    [Theory]
    [InlineData("abc")]
    [InlineData("1")]
    [InlineData("500")]
    [InlineData("")]
    public void Option_DurationInvalid_ShowsErrorAndDoesNotCommit(string input)
    {
        var definition = new ProviderOptionDefinition("Poll", "Poll", ProviderOptionType.Duration, "00:00:05", Min: 2, Max: 60);
        var commits = 0;
        var option = new OptionViewModel(definition, null, () => commits++);

        option.Text = input;

        commits.Should().Be(0);
        option.Error.Should().NotBeEmpty();
        option.StoredValue.Should().BeNull();
    }

    /// <summary>
    /// Verifies switch and pick list options commit on change.
    /// </summary>
    [Fact]
    public void Option_BoolAndChoice_CommitOnChange()
    {
        var commits = 0;
        var flag = new OptionViewModel(new ProviderOptionDefinition("f", "f", ProviderOptionType.Bool, "false"), null, () => commits++);
        var pick = new OptionViewModel(new ProviderOptionDefinition("c", "c", ProviderOptionType.Choice, "a", Choices: ["a", "b"]), null, () => commits++);

        flag.BoolValue = true;
        pick.SelectedChoice = "b";

        commits.Should().Be(2);
        flag.StoredValue.Should().Be("true");
        pick.StoredValue.Should().Be("b");
    }

    /// <summary>
    /// Verifies turning the flash off is announced, so a view can stop a flash that is playing.
    /// </summary>
    [Fact]
    public void FlashEnabled_Changed_RaisesPropertyChanged()
    {
        var row = new AssetRowViewModel(Btc, "p");
        List<string?> changes = [];
        row.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        row.RollEnabled = true;
        row.RollEnabled = false;

        changes.Should().Equal(nameof(AssetRowViewModel.RollEnabled), nameof(AssetRowViewModel.RollEnabled));
    }
}

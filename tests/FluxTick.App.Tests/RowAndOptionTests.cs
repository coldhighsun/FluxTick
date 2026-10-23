using FluxTick.Abstractions;
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
    /// Verifies the flash counter only advances when the price actually changes, with the right direction.
    /// </summary>
    [Fact]
    public void Update_FlashVersion_AdvancesOnlyOnPriceChange()
    {
        var row = new AssetRowViewModel(Btc, "p") { FlashEnabled = true };

        row.Update(Snapshot(100m, 100m));
        row.FlashVersion.Should().Be(0, "the first quote has nothing to compare with");

        row.Update(Snapshot(101m, 100m));
        row.FlashVersion.Should().Be(1);
        row.FlashUp.Should().BeTrue();

        row.Update(Snapshot(101m, 100m));
        row.FlashVersion.Should().Be(1);

        row.Update(Snapshot(99m, 100m));
        row.FlashVersion.Should().Be(2);
        row.FlashUp.Should().BeFalse();
        row.IsDown.Should().BeTrue();
    }

    /// <summary>
    /// Verifies a disabled flash keeps the counter still while the price still updates, and resumes once re-enabled.
    /// </summary>
    [Fact]
    public void Update_FlashDisabled_DoesNotAdvanceFlashVersion()
    {
        var row = new AssetRowViewModel(Btc, "p");
        row.FlashEnabled.Should().BeFalse("the flash is off unless the setting turns it on");

        row.Update(Snapshot(100m, 100m));
        row.Update(Snapshot(101m, 100m));

        row.FlashVersion.Should().Be(0);
        row.PriceText.Should().Be("101.00");

        row.FlashEnabled = true;
        row.Update(Snapshot(101m, 100m));
        row.FlashVersion.Should().Be(0, "the price did not change since the last quote");

        row.Update(Snapshot(102m, 100m));
        row.FlashVersion.Should().Be(1);
        row.FlashUp.Should().BeTrue();
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

        row.FlashEnabled = true;
        row.FlashEnabled = false;

        changes.Should().Equal(nameof(AssetRowViewModel.FlashEnabled), nameof(AssetRowViewModel.FlashEnabled));
    }
}

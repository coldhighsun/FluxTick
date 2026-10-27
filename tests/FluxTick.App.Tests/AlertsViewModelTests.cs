using FluxTick.Abstractions;
using FluxTick.App.Services;
using FluxTick.App.ViewModels;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the price alert editor and the list of alerts.
/// </summary>
public class AlertsViewModelTests
{
    /// <summary>
    /// The Bitcoin test asset.
    /// </summary>
    private static readonly Asset Btc = new(Harness.Crypto, "BTC", "Bitcoin", AssetClass.Crypto);

    /// <summary>
    /// The Apple test asset.
    /// </summary>
    private static readonly Asset Aapl = new(Harness.Stocks, "AAPL", "Apple", AssetClass.Equity);

    /// <summary>
    /// Verifies a valid form adds a persisted rule for the instrument of the editor and clears the form.
    /// </summary>
    [Fact]
    public async Task Add_ValidForm_PersistsRuleForTheSubject()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc, Aapl));
        h.Alerts.Begin(Btc);
        h.Alerts.SelectedCondition = AlertCondition.PriceBelow;
        h.Alerts.ThresholdText = " 5.5 ";
        h.Alerts.NewRepeat = true;

        await h.Alerts.AddCommand.ExecuteAsync(null);

        var rule = h.Store.Stored!.Alerts.Should().ContainSingle().Subject;
        rule.AssetId.Should().Be(Btc.Id);
        rule.Condition.Should().Be(AlertCondition.PriceBelow);
        rule.Threshold.Should().Be(5.5m);
        rule.Repeat.Should().BeTrue();
        rule.Enabled.Should().BeTrue();
        h.Alerts.ThresholdText.Should().BeEmpty();
        h.Alerts.Items.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies a new alert repeats unless the user unchecks it, and that opening the editor again restores that.
    /// </summary>
    [Fact]
    public async Task Begin_RepeatIsCheckedByDefault()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc, Aapl));
        h.Alerts.NewRepeat.Should().BeTrue();

        h.Alerts.Begin(Btc);
        h.Alerts.NewRepeat = false;
        h.Alerts.Begin(Aapl);

        h.Alerts.NewRepeat.Should().BeTrue();
    }

    /// <summary>
    /// Verifies a threshold that is not a price adds nothing and says why.
    /// </summary>
    /// <param name="text">
    /// The typed threshold.
    /// </param>
    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-3")]
    public async Task Add_InvalidThreshold_AddsNothingAndExplains(string text)
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));
        h.Alerts.Begin(Btc);
        h.Alerts.ThresholdText = text;

        await h.Alerts.AddCommand.ExecuteAsync(null);

        h.Store.Stored!.Alerts.Should().BeEmpty();
        h.Alerts.HasMessage.Should().BeTrue();
    }

    /// <summary>
    /// Verifies an instrument cannot get a second alert with the same condition, and says why.
    /// </summary>
    [Fact]
    public async Task Add_ConditionAlreadyTaken_AddsNothingAndExplains()
    {
        var settings = Harness.WatchlistOf(Btc);
        settings.Alerts.Add(new AlertRule { ProviderId = Btc.ProviderId, Symbol = Btc.Symbol, Condition = AlertCondition.PriceAbove, Threshold = 100 });
        await using var h = new Harness(settings);
        h.Alerts.Begin(Btc);
        h.Alerts.SelectedCondition = AlertCondition.PriceAbove;
        h.Alerts.ThresholdText = "200";

        await h.Alerts.AddCommand.ExecuteAsync(null);

        h.Store.Stored!.Alerts.Should().ContainSingle();
        h.Alerts.HasMessage.Should().BeTrue();

        // The other condition is still free.
        h.Alerts.SelectedCondition = AlertCondition.PriceBelow;
        h.Alerts.HasMessage.Should().BeFalse();
        await h.Alerts.AddCommand.ExecuteAsync(null);
        h.Store.Stored!.Alerts.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies the form opens with a condition the instrument has no alert for, so it is ready to use, and only warns
    /// when the instrument has both.
    /// </summary>
    [Fact]
    public async Task Begin_PicksAFreeCondition_AndWarnsOnlyWhenNoneIsFree()
    {
        var settings = Harness.WatchlistOf(Btc);
        settings.Alerts.Add(new AlertRule { ProviderId = Btc.ProviderId, Symbol = Btc.Symbol, Condition = AlertCondition.PriceAbove, Threshold = 100 });
        await using var h = new Harness(settings);

        h.Alerts.Begin(Btc);

        h.Alerts.SelectedCondition.Should().Be(AlertCondition.PriceBelow);
        h.Alerts.HasMessage.Should().BeFalse();

        await h.Settings.UpdateAsync(s => s.Alerts.Add(new AlertRule { ProviderId = Btc.ProviderId, Symbol = Btc.Symbol, Condition = AlertCondition.PriceBelow, Threshold = 50 }));
        h.Alerts.Begin(Btc);

        h.Alerts.HasMessage.Should().BeTrue();
    }

    /// <summary>
    /// Verifies another instrument, or the same symbol from another data source, is not affected by the limit.
    /// </summary>
    [Fact]
    public async Task Add_SameConditionOnAnotherInstrument_IsAllowed()
    {
        var settings = Harness.WatchlistOf(Btc, Aapl);
        settings.Alerts.Add(new AlertRule { ProviderId = Btc.ProviderId, Symbol = Btc.Symbol, Condition = AlertCondition.PriceAbove, Threshold = 100 });
        await using var h = new Harness(settings);
        h.Alerts.Begin(Aapl);
        h.Alerts.ThresholdText = "50";

        await h.Alerts.AddCommand.ExecuteAsync(null);

        h.Store.Stored!.Alerts.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies a saved rule cannot be changed to a condition the instrument already has, and says why.
    /// </summary>
    [Fact]
    public async Task Item_ChangeToTakenCondition_IsFlaggedAndCannotBeSaved()
    {
        var settings = Harness.WatchlistOf(Btc);
        settings.Alerts.Add(new AlertRule { Id = "up", ProviderId = Btc.ProviderId, Symbol = Btc.Symbol, Condition = AlertCondition.PriceAbove, Threshold = 100 });
        settings.Alerts.Add(new AlertRule { Id = "down", ProviderId = Btc.ProviderId, Symbol = Btc.Symbol, Condition = AlertCondition.PriceBelow, Threshold = 50 });
        await using var h = new Harness(settings);
        h.Alerts.Begin(Btc);
        var down = h.Alerts.Items.Single(i => i.Id == "down");

        down.SelectedCondition = AlertCondition.PriceAbove;

        down.HasError.Should().BeTrue();
        down.SaveCommand.CanExecute(null).Should().BeFalse();

        down.SelectedCondition = AlertCondition.PriceBelow;
        down.HasError.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the editor lists only the rules of its instrument, and the settings page lists all of them.
    /// </summary>
    [Fact]
    public async Task Items_EditorShowsOneInstrument_SettingsPageShowsAll()
    {
        var settings = Harness.WatchlistOf(Btc, Aapl);
        settings.Alerts.Add(new AlertRule { ProviderId = Btc.ProviderId, Symbol = Btc.Symbol, Threshold = 1 });
        settings.Alerts.Add(new AlertRule { ProviderId = Aapl.ProviderId, Symbol = Aapl.Symbol, Threshold = 2 });
        await using var h = new Harness(settings);

        h.Alerts.Begin(Aapl);

        h.Alerts.Items.Select(i => i.Symbol).Should().Equal("AAPL");
        h.SettingsPage.Alerts.Items.Select(i => i.Symbol).Should().BeEquivalentTo(["BTC", "AAPL"]);
    }

    /// <summary>
    /// Verifies a rule names the data source of its instrument, so that the same symbol from two sources can be told apart.
    /// </summary>
    [Fact]
    public async Task Items_ShowTheProviderOfEachInstrument()
    {
        var sameSymbol = new Asset(Harness.Stocks, "BTC", "Bitcoin ETF", AssetClass.Equity);
        var settings = Harness.WatchlistOf(Btc, sameSymbol);
        settings.Alerts.Add(new AlertRule { ProviderId = Btc.ProviderId, Symbol = Btc.Symbol, Threshold = 1 });
        settings.Alerts.Add(new AlertRule { ProviderId = sameSymbol.ProviderId, Symbol = sameSymbol.Symbol, Threshold = 2 });
        await using var h = new Harness(settings);

        var names = h.SettingsPage.Alerts.Items.Select(i => i.ProviderName).ToList();

        names.Should().HaveCount(2).And.OnlyHaveUniqueItems().And.NotContain(string.Empty);
    }

    /// <summary>
    /// Verifies the switches of a rule are saved, and deleting removes it.
    /// </summary>
    [Fact]
    public async Task Item_SwitchesAndDelete_AreSaved()
    {
        var settings = Harness.WatchlistOf(Btc);
        settings.Alerts.Add(new AlertRule { ProviderId = Btc.ProviderId, Symbol = Btc.Symbol, Threshold = 100 });
        await using var h = new Harness(settings);
        h.Alerts.Begin(Btc);
        var item = h.Alerts.Items.Single();

        item.Enabled = false;
        item.Repeat = true;

        await Harness.EventuallyAsync(() => h.Store.Stored!.Alerts.Single() is { Enabled: false, Repeat: true });

        await item.DeleteCommand.ExecuteAsync(null);

        h.Store.Stored!.Alerts.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies the condition and the threshold of a saved rule only change when they are saved, not while they are typed.
    /// </summary>
    [Fact]
    public async Task Item_EditConditionAndThreshold_TakeEffectOnlyOnSave()
    {
        var settings = Harness.WatchlistOf(Btc);
        settings.Alerts.Add(new AlertRule { ProviderId = Btc.ProviderId, Symbol = Btc.Symbol, Threshold = 100 });
        await using var h = new Harness(settings);
        h.Alerts.Begin(Btc);
        var item = h.Alerts.Items.Single();
        item.SaveCommand.CanExecute(null).Should().BeFalse();

        item.SelectedCondition = AlertCondition.PriceBelow;
        item.ThresholdText = " 80.5 ";

        item.IsDirty.Should().BeTrue();
        item.SaveCommand.CanExecute(null).Should().BeTrue();
        h.Store.Stored!.Alerts.Single().Threshold.Should().Be(100m);
        h.Alerts.Items.Single().Should().BeSameAs(item);

        item.SaveCommand.Execute(null);

        item.IsDirty.Should().BeFalse();
        await Harness.EventuallyAsync(() => h.Store.Stored!.Alerts.Single() is { Condition: AlertCondition.PriceBelow, Threshold: 80.5m });
        h.Alerts.HasMessage.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a threshold that cannot be used is flagged as soon as it is typed and cannot be saved.
    /// </summary>
    /// <param name="text">
    /// The typed threshold.
    /// </param>
    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-3")]
    public async Task Item_InvalidThreshold_IsFlaggedAndCannotBeSaved(string text)
    {
        var settings = Harness.WatchlistOf(Btc);
        settings.Alerts.Add(new AlertRule { ProviderId = Btc.ProviderId, Symbol = Btc.Symbol, Threshold = 100 });
        await using var h = new Harness(settings);
        h.Alerts.Begin(Btc);
        var item = h.Alerts.Items.Single();

        item.ThresholdText = text;

        item.HasError.Should().BeTrue();
        item.Error.Should().NotBeEmpty();
        item.SaveCommand.CanExecute(null).Should().BeFalse();
        h.Store.Stored!.Alerts.Single().Threshold.Should().Be(100m);
    }

    /// <summary>
    /// Verifies what was typed and not saved is gone when the window is opened again, and the saved value shows.
    /// </summary>
    [Fact]
    public async Task Begin_AfterUnsavedEdit_ShowsTheSavedValueAgain()
    {
        var settings = Harness.WatchlistOf(Btc);
        settings.Alerts.Add(new AlertRule { ProviderId = Btc.ProviderId, Symbol = Btc.Symbol, Threshold = 100 });
        await using var h = new Harness(settings);
        h.Alerts.Begin(Btc);
        h.Alerts.Items.Single().ThresholdText = "abc";

        h.Alerts.Begin(Btc);

        var item = h.Alerts.Items.Single();
        item.ThresholdText.Should().Be("100");
        item.HasError.Should().BeFalse();
        item.IsDirty.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the settings page shows the saved values again when it is opened again.
    /// </summary>
    [Fact]
    public async Task SettingsReload_AfterUnsavedEdit_ShowsTheSavedValueAgain()
    {
        var settings = Harness.WatchlistOf(Btc);
        settings.Alerts.Add(new AlertRule { ProviderId = Btc.ProviderId, Symbol = Btc.Symbol, Threshold = 100 });
        await using var h = new Harness(settings);
        h.SettingsPage.Alerts.Items.Single().ThresholdText = "abc";

        h.SettingsPage.Reload();

        h.SettingsPage.Alerts.Items.Single().ThresholdText.Should().Be("100");
    }

    /// <summary>
    /// Verifies the form warns about a threshold that cannot be used while it is typed, and stops warning when it is fixed.
    /// </summary>
    [Fact]
    public async Task Form_InvalidThreshold_WarnsWhileTyping()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));
        h.Alerts.Begin(Btc);

        h.Alerts.ThresholdText = "0";
        h.Alerts.HasMessage.Should().BeTrue();

        h.Alerts.ThresholdText = "12.5";
        h.Alerts.HasMessage.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a row shows the bell while an enabled rule watches it.
    /// </summary>
    [Fact]
    public async Task Row_HasAlert_FollowsEnabledRules()
    {
        var settings = Harness.WatchlistOf(Btc, Aapl);
        settings.Alerts.Add(new AlertRule { ProviderId = Btc.ProviderId, Symbol = Btc.Symbol, Threshold = 100 });
        settings.Alerts.Add(new AlertRule { ProviderId = Aapl.ProviderId, Symbol = Aapl.Symbol, Threshold = 100, Enabled = false });
        await using var h = new Harness(settings);

        await h.Main.InitializeAsync();

        h.Main.Rows.Single(r => r.Symbol == "BTC").HasAlert.Should().BeTrue();
        h.Main.Rows.Single(r => r.Symbol == "AAPL").HasAlert.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a row carries the levels of its enabled alerts for the sparkline to draw, and not those of disabled ones.
    /// </summary>
    [Fact]
    public async Task Row_AlertLevels_FollowEnabledRules()
    {
        var settings = Harness.WatchlistOf(Btc);
        settings.Alerts.Add(new AlertRule { ProviderId = Btc.ProviderId, Symbol = Btc.Symbol, Condition = AlertCondition.PriceAbove, Threshold = 120 });
        settings.Alerts.Add(new AlertRule { ProviderId = Btc.ProviderId, Symbol = Btc.Symbol, Condition = AlertCondition.PriceBelow, Threshold = 80, Enabled = false });
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();
        var row = h.Main.Rows.Single();

        row.AlertAbove.Should().Be(120m);
        row.AlertBelow.Should().BeNull();

        await h.Settings.UpdateAsync(s => s.Alerts.Single(a => a.Condition == AlertCondition.PriceBelow).Enabled = true);

        await Harness.EventuallyAsync(() => row.AlertBelow == 80m);
    }

    /// <summary>
    /// Verifies an alert that fired makes the row of its instrument flash, in the direction of the alert, whether or not
    /// the flash on price changes is on, and leaves the other rows alone.
    /// </summary>
    [Fact]
    public async Task FlashAlert_FlashesOnlyTheRowOfTheInstrument()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc, Aapl));
        await h.Main.InitializeAsync();
        h.Main.Rows.Should().OnlyContain(r => !r.RollEnabled);
        var rule = new AlertRule { ProviderId = Btc.ProviderId, Symbol = Btc.Symbol, Condition = AlertCondition.PriceBelow, Threshold = 50 };
        var quote = new Quote(Btc.Id, 49m, null, DateTimeOffset.UnixEpoch);

        h.Main.FlashAlert(new AlertTriggeredEventArgs(rule, Btc, quote));

        var btc = h.Main.Rows.Single(r => r.Symbol == "BTC");
        btc.AlertFlashVersion.Should().Be(1);
        btc.AlertFlashUp.Should().BeFalse();
        h.Main.Rows.Single(r => r.Symbol == "AAPL").AlertFlashVersion.Should().Be(0);

        rule.Condition = AlertCondition.PriceAbove;
        h.Main.FlashAlert(new AlertTriggeredEventArgs(rule, Btc, quote));

        btc.AlertFlashVersion.Should().Be(2);
        btc.AlertFlashUp.Should().BeTrue();
        btc.RollVersion.Should().Be(0, "the flash of the price is a different one");
    }

    /// <summary>
    /// Verifies the text of a rule names the condition and the threshold.
    /// </summary>
    [Fact]
    public void Summary_NamesConditionAndThreshold()
    {
        var rule = new AlertRule { Condition = AlertCondition.PriceBelow, Threshold = 1234.5m };

        AlertText.Summary(rule).Should().Contain("1234.5").And.Contain("below");
    }

    /// <summary>
    /// Verifies that the same threshold written another way is not a change, and another number is.
    /// </summary>
    [Fact]
    public async Task IsDirty_SameNumberWrittenAnotherWay_IsNotAChange()
    {
        var settings = Harness.WatchlistOf(Btc);
        settings.Alerts.Add(new AlertRule { ProviderId = Btc.ProviderId, Symbol = Btc.Symbol, Threshold = 5.5m });
        await using var h = new Harness(settings);
        h.Alerts.Begin(Btc);
        var item = h.Alerts.Items.Single();

        TestCulture.Under("de-DE", () =>
        {
            item.ThresholdText = "5.5";
            item.IsDirty.Should().BeFalse();
            item.ThresholdText = "5,50";
            item.IsDirty.Should().BeFalse();
            item.ThresholdText = "5,6";
            item.IsDirty.Should().BeTrue();
            item.ThresholdText = "abc";
            item.IsDirty.Should().BeTrue();
        });
    }
}

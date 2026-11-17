using FluxTick.Abstractions;
using FluxTick.Core.Application;

namespace FluxTick.Core.Tests;

/// <summary>
/// Tests for the description of what a settings change altered, which the log shows.
/// </summary>
public class SettingsChangeDescriptionTests
{
    /// <summary>
    /// Verifies a changed value is named by its path two levels deep.
    /// </summary>
    [Fact]
    public void DescribeChanges_WindowMoved_NamesTheChangedProperties()
    {
        var before = new AppSettings();
        var after = before.Clone();
        after.Window.X = 10;
        after.Window.Width = 500;

        SettingsManager.DescribeChanges(before, after).Should().Be("window.x, window.width");
    }

    /// <summary>
    /// Verifies a list is named as a whole, whatever changed inside it.
    /// </summary>
    [Fact]
    public void DescribeChanges_WatchlistEntryAdded_NamesTheList()
    {
        var before = new AppSettings();
        var after = before.Clone();
        after.Watchlist.Add(WatchlistEntry.From(new Asset("yahoo", "AAPL", "Apple", AssetClass.Equity)));

        SettingsManager.DescribeChanges(before, after).Should().Be("watchlist");
    }

    /// <summary>
    /// Verifies the value of a secret option is not part of the description, only where it changed.
    /// </summary>
    [Fact]
    public void DescribeChanges_OptionChanged_NamesTheProviderWithoutTheValue()
    {
        var before = new AppSettings();
        var after = before.Clone();
        after.Providers["eulerpool"] = new ProviderSettings { Options = { ["ApiKey"] = "super-secret" } };

        var description = SettingsManager.DescribeChanges(before, after);

        description.Should().Be("providers.eulerpool");
        description.Should().NotContain("super-secret");
    }

    /// <summary>
    /// Verifies a change that leaves the settings as they were says so.
    /// </summary>
    [Fact]
    public void DescribeChanges_NothingDiffers_SaysSo()
    {
        var before = new AppSettings();

        SettingsManager.DescribeChanges(before, before.Clone()).Should().Be("(normalized only)");
    }
}

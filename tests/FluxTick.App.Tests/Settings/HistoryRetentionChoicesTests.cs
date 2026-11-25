using FluxTick.App.ViewModels;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for how the days kept in the settings are shown as one of the choices of the settings page.
/// </summary>
public class HistoryRetentionChoicesTests
{
    /// <summary>
    /// Verifies that a number of days that is a choice maps to it, and any other to the closest one.
    /// </summary>
    /// <param name="days">
    /// The days in the settings.
    /// </param>
    /// <param name="expected">
    /// The choice that is shown.
    /// </param>
    [Theory]
    [InlineData(30, HistoryRetention.Month)]
    [InlineData(365, HistoryRetention.Year)]
    [InlineData(1825, HistoryRetention.FiveYears)]
    [InlineData(1, HistoryRetention.Month)]
    [InlineData(3650, HistoryRetention.FiveYears)]
    [InlineData(100, HistoryRetention.Quarter)]
    [InlineData(300, HistoryRetention.Year)]
    [InlineData(135, HistoryRetention.HalfYear)]
    public void Nearest_Days_ReturnsTheClosestChoice(int days, HistoryRetention expected)
    {
        var choice = HistoryRetentionChoices.Nearest(days);

        choice.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that two choices that are equally far away give the longer one, so that data is not removed earlier than
    /// the file said.
    /// </summary>
    [Fact]
    public void Nearest_EquallyCloseToTwoChoices_ReturnsTheLongerOne()
    {
        // 60 days is 30 from a month and 30 from three months.
        HistoryRetentionChoices.Nearest(60).Should().Be(HistoryRetention.Quarter);
    }
}

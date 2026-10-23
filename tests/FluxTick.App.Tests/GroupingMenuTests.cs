using FluxTick.App.Views;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for <see cref="GroupingMenu"/>.
/// </summary>
public class GroupingMenuTests
{
    /// <summary>
    /// Verifies every grouping has its own readable label, which is not the enum name. A grouping added later fails
    /// here until it is named, so the menus and the settings page cannot show a raw name.
    /// </summary>
    [Fact]
    public void Label_EveryGrouping_HasDistinctReadableLabel()
    {
        var groupings = Enum.GetValues<CardGrouping>();

        var labels = groupings.Select(GroupingMenu.Label).ToList();

        labels.Should().OnlyHaveUniqueItems();
        for (var i = 0; i < groupings.Length; i++)
        {
            labels[i].Should().NotBe(groupings[i].ToString());
        }
    }
}

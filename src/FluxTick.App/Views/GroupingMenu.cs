using System.Collections;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using FluxTick.App.ViewModels;
using FluxTick.Core.Application;

namespace FluxTick.App.Views;

/// <summary>
/// The one place that knows how the card groupings are named and listed in menus, so that the header button, the
/// context menu and the settings page cannot drift apart.
/// </summary>
public static class GroupingMenu
{
    /// <summary>
    /// Marks the menu entries that this class added, so that they can be replaced the next time.
    /// </summary>
    private const string EntryTag = "grouping-menu";

    /// <summary>
    /// Gets a converter that shows a grouping by its label, for lists that offer the groupings.
    /// </summary>
    public static IValueConverter LabelConverter { get; } = new FuncValueConverter<CardGrouping, string>(grouping => Label(grouping));

    /// <summary>
    /// Gets the text that names a grouping.
    /// </summary>
    /// <param name="grouping">
    /// The grouping.
    /// </param>
    /// <returns>
    /// The label.
    /// </returns>
    public static string Label(CardGrouping grouping) => grouping switch
    {
        CardGrouping.None => "No grouping",
        CardGrouping.ByType => "By type",
        CardGrouping.ByProvider => "By data source",
        CardGrouping.Custom => "Custom groups",
        _ => grouping.ToString(),
    };

    /// <summary>
    /// Puts one radio entry per grouping at the start of a menu, the current one checked, replacing the entries that
    /// an earlier call added. Entries that were not added by this class stay where they are, after the new ones.
    /// </summary>
    /// <param name="items">
    /// The items of the menu or sub menu.
    /// </param>
    /// <param name="viewModel">
    /// The main view model, which holds the current grouping and the command that changes it.
    /// </param>
    public static void Fill(IList items, MainViewModel viewModel)
    {
        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (items[i] is Control { Tag: EntryTag })
            {
                items.RemoveAt(i);
            }
        }

        var position = 0;
        foreach (var grouping in Enum.GetValues<CardGrouping>())
        {
            items.Insert(position++, new MenuItem
            {
                Header = Label(grouping),
                Tag = EntryTag,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = viewModel.Grouping == grouping,
                Command = viewModel.SetGroupingCommand,
                CommandParameter = grouping,
            });
        }
    }
}

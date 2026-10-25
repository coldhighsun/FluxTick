using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using FluxTick.App.Localization;
using FluxTick.App.ViewModels;

namespace FluxTick.App.Views;

/// <summary>
/// The settings page: appearance, groups, data sources and routing.
/// </summary>
public sealed partial class SettingsView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsView"/> class.
    /// </summary>
    public SettingsView()
    {
        InitializeComponent();

        // The controller listens to the list for as long as the page exists.
        _ = new ListDragController(GroupsList, GroupsScroller, MoveGroupAsync);
        _ = new ListDragController(TrayAssetsList, TrayAssetsScroller, MoveTrayAssetAsync);
    }

    /// <summary>
    /// Moves the instrument that was dragged from one position of the tray list to another.
    /// </summary>
    /// <param name="from">
    /// The original position.
    /// </param>
    /// <param name="to">
    /// The new position.
    /// </param>
    /// <returns>
    /// A task that completes once the new order is saved.
    /// </returns>
    private Task MoveTrayAssetAsync(int from, int to) =>
        DataContext is SettingsViewModel { TrayAssets: { } assets } && from >= 0 && from < assets.Items.Count
            ? assets.MoveToAsync(assets.Items[from], to)
            : Task.CompletedTask;

    /// <summary>
    /// Moves the group that was dragged from one position of the list to another.
    /// </summary>
    /// <param name="from">
    /// The original position.
    /// </param>
    /// <param name="to">
    /// The new position.
    /// </param>
    /// <returns>
    /// A task that completes once the new order is saved.
    /// </returns>
    private Task MoveGroupAsync(int from, int to) =>
        DataContext is SettingsViewModel { Groups: { } groups } && from >= 0 && from < groups.Items.Count
            ? groups.MoveToAsync(groups.Items[from], to)
            : Task.CompletedTask;

    /// <summary>
    /// Asks for the name of a new group next to the placeholder, the same way the card layout does.
    /// </summary>
    /// <param name="sender">
    /// The placeholder.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnNewGroupClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel { Groups: { } groups })
        {
            GroupNamePrompt.Show(NewGroupButton, Loc.Get("Group_NewTitle"), PlacementMode.TopEdgeAlignedLeft, groups.AddAsync);
        }
    }
}

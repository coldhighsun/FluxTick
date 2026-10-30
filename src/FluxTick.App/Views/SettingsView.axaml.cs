using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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

        // A font is used when the pointer goes down on it, before the box can lose the focus to the pop-up and close the list.
        FontList.AddHandler(PointerPressedEvent, OnFontListPressed, RoutingStrategies.Tunnel);

        // The controller listens to the list for as long as the page exists.
        _ = new ListDragController(GroupsList, GroupsScroller, MoveGroupAsync);
        _ = new ListDragController(TrayAssetsList, TrayAssetsScroller, MoveTrayAssetAsync);
    }

    /// <summary>
    /// Opens the list of fonts and selects the text when the font box gets the focus, so that a font can be picked without
    /// typing and a name typed at once replaces the old one.
    /// </summary>
    /// <param name="sender">
    /// The font box.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnFontBoxGotFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && DataContext is SettingsViewModel viewModel)
        {
            box.SelectAll();
            viewModel.OpenFontList();
        }
    }

    /// <summary>
    /// Uses the typed font when the font box is left with the name of an installed font, and else closes the list and
    /// shows the font in use again.
    /// </summary>
    /// <param name="sender">
    /// The font box.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnFontBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { IsKeyboardFocusWithin: false } && DataContext is SettingsViewModel viewModel)
        {
            viewModel.LeaveFontBox();
        }
    }

    /// <summary>
    /// Opens the list of fonts again when the box is clicked, since the list may have been closed by a click on a font, by
    /// Escape or by a click next to it. It is done when the button is released, after a click on the box has dismissed the
    /// list that was open, so that the click does not leave it closed.
    /// </summary>
    /// <param name="sender">
    /// The font box.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnFontBoxPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.OpenFontList();
        }
    }

    /// <summary>
    /// Handles the keys of the font box: Escape closes the list and goes back to the font in use, Down opens the list, and
    /// Enter uses the font the typed text stands for.
    /// </summary>
    /// <param name="sender">
    /// The font box.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnFontBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                viewModel.LeaveFontBox();
                e.Handled = true;
                break;
            case Key.Down:
                viewModel.OpenFontList();
                e.Handled = true;
                break;
            case Key.Enter:
                viewModel.CommitFont();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Uses the font that was pressed in the list.
    /// </summary>
    /// <param name="sender">
    /// The list.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnFontListPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(null).Properties.IsLeftButtonPressed
            && e.Source is Visual source
            && source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: string family }
            && DataContext is SettingsViewModel viewModel)
        {
            viewModel.ChooseFont(family);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Tells the view model the list is closed, whatever closed it, so that it opens again the next time. When the box has
    /// not kept the focus, as after a click next to the list, it shows the font in use again.
    /// </summary>
    /// <param name="sender">
    /// The pop-up.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnFontListClosed(object? sender, EventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        viewModel.IsFontListOpen = false;
        if (!FontBox.IsKeyboardFocusWithin)
        {
            viewModel.LeaveFontBox();
        }
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
    /// Asks for the name of a new group next to the placeholder.
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

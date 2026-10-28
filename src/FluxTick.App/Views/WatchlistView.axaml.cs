using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluxTick.App.Localization;
using FluxTick.App.ViewModels;
using FluxTick.Core.Application;

namespace FluxTick.App.Views;

/// <summary>
/// The scrolling list of watched instruments. Rows can be reordered by pressing and dragging them vertically.
/// </summary>
public sealed partial class WatchlistView : UserControl
{
    /// <summary>
    /// Marks the menu entries that are added dynamically to a card's context menu.
    /// </summary>
    private const string GroupMenuTag = "group-menu";

    /// <summary>
    /// How long the hint about the automatic switch to the custom grouping stays.
    /// </summary>
    private static readonly TimeSpan HintDuration = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Handles dragging of the cards in the card layout.
    /// </summary>
    private readonly CardDragController _cardDrag;

    /// <summary>
    /// Handles dragging of the group headers in the card layout.
    /// </summary>
    private readonly GroupDragController _groupDrag;

    /// <summary>
    /// The view model whose changes are observed to time the automatic switch hint.
    /// </summary>
    private MainViewModel? _observed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchlistView"/> class.
    /// </summary>
    public WatchlistView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            _observed?.PropertyChanged -= OnMainPropertyChanged;
            _observed = DataContext as MainViewModel;
            _observed?.PropertyChanged += OnMainPropertyChanged;
        };
        SectionsHost.AddHandler(PointerPressedEvent, OnSectionsPointerPressedTunnel, RoutingStrategies.Tunnel);
        _cardDrag = new CardDragController(SectionsHost, CardsScroller, DropIndicator, this, () => DataContext as MainViewModel);
        _groupDrag = new GroupDragController(SectionsHost, CardsScroller, this, () => DataContext as MainViewModel);
        SectionsHost.AddHandler(ContextRequestedEvent, OnSectionsContextRequested, RoutingStrategies.Tunnel);
        Root.AddHandler(ContextRequestedEvent, OnRootContextRequested, RoutingStrategies.Tunnel);
        _ = new ListDragController(RowsHost, Scroller, MoveRowAsync, LiftRow);
    }

    /// <summary>
    /// Fills the grouping sub menu of the context menu of the empty area just before it opens, so that it shows the
    /// current grouping.
    /// </summary>
    /// <param name="sender">
    /// The root of the view.
    /// </param>
    /// <param name="e">
    /// The context request data.
    /// </param>
    private void OnRootContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            GroupingMenu.Fill(GroupCardsMenu.Items, vm);
        }
    }

    /// <summary>
    /// Prepares the menus that depend on what was right-clicked, before the click opens them. This runs while the
    /// event tunnels down, because a menu that was never opened has no data context yet, so its own Opening event
    /// cannot tell which card or header it belongs to the first time.
    /// </summary>
    /// <param name="sender">
    /// The sections host.
    /// </param>
    /// <param name="e">
    /// The context request data.
    /// </param>
    private void OnSectionsContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is not Visual source)
        {
            return;
        }

        if (source.FindAncestorOfType<AssetCardView>(includeSelf: true) is { DataContext: AssetRowViewModel row, ContextMenu: { } cardMenu } card)
        {
            PrepareCardMenu(cardMenu, row, card);
            return;
        }

        // Only user-defined groups have a header menu; type and data source sections have none.
        if (source.FindAncestorOfType<ToggleButton>(includeSelf: true) is { Name: "SectionHeader", DataContext: WatchSectionViewModel { Kind: not WatchSectionKind.Group } })
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Adds the "Move to group" entries to the menu of a card. The entries depend on the groups that exist at that
    /// moment, so they are rebuilt every time.
    /// </summary>
    /// <param name="menu">
    /// The card's context menu.
    /// </param>
    /// <param name="row">
    /// The row of the card.
    /// </param>
    /// <param name="anchor">
    /// The card, which pop-ups started from the menu open next to.
    /// </param>
    private void PrepareCardMenu(ContextMenu menu, AssetRowViewModel row, Control anchor)
    {
        for (var i = menu.Items.Count - 1; i >= 0; i--)
        {
            if (menu.Items[i] is Control { Tag: GroupMenuTag })
            {
                menu.Items.RemoveAt(i);
            }
        }

        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var newGroup = new MenuItem { Header = Loc.Get("Main_NewGroup"), Tag = GroupMenuTag };
        newGroup.Click += (_, _) => GroupNamePrompt.Show(anchor, Loc.Get("Group_NewTitle"), PlacementMode.Pointer, name => vm.CreateGroupAsync(name, row));

        // Only the custom grouping shows the groups, so only there can the card be moved into an existing one.
        // In the other groupings creating a group switches to the custom grouping.
        if (vm.Grouping != CardGrouping.Custom)
        {
            newGroup.Header = Loc.Get("Watch_MoveToNewGroup");
            menu.Items.Insert(0, newGroup);
            menu.Items.Insert(1, new Separator { Tag = GroupMenuTag });
            return;
        }

        var moveTo = new MenuItem { Header = Loc.Get("Watch_MoveToGroup"), Tag = GroupMenuTag };
        foreach (var choice in vm.GetGroupChoices(row))
        {
            var target = choice.GroupId;
            var item = new MenuItem
            {
                Header = choice.Title,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = choice.IsCurrent,
            };
            item.Click += (_, _) => _ = vm.MoveToGroupAsync(row, target);
            moveTo.Items.Add(item);
        }

        moveTo.Items.Add(new Separator());
        newGroup.Header = Loc.Get("Main_NewGroup");
        moveTo.Items.Add(newGroup);
        menu.Items.Insert(0, moveTo);
        menu.Items.Insert(1, new Separator { Tag = GroupMenuTag });
    }

    /// <summary>
    /// Starts renaming a group in place when its header is double-clicked. The first click of the double click has
    /// already collapsed or expanded the section, so that is undone, and the second click is swallowed.
    /// </summary>
    /// <param name="sender">
    /// The sections host.
    /// </param>
    /// <param name="e">
    /// The pointer event data.
    /// </param>
    private void OnSectionsPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 2 || !e.GetCurrentPoint(SectionsHost).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var header = (e.Source as Visual)?.FindAncestorOfType<ToggleButton>(includeSelf: true);
        if (header is not { Name: "SectionHeader", DataContext: WatchSectionViewModel { Kind: WatchSectionKind.Group } section })
        {
            return;
        }

        e.Handled = true;
        section.IsCollapsed = !section.IsCollapsed;
        StartRename(header, section);
    }

    /// <summary>
    /// Swaps the title of a group for the rename box and puts the caret in it with the old name selected.
    /// </summary>
    /// <param name="header">
    /// The header button of the group; the rename box is its sibling.
    /// </param>
    /// <param name="section">
    /// The section of the group.
    /// </param>
    private static void StartRename(ToggleButton header, WatchSectionViewModel section)
    {
        section.BeginRename();
        var box = header.GetVisualParent()?.GetVisualChildren().OfType<TextBox>().FirstOrDefault();
        Dispatcher.UIThread.Post(
            () =>
            {
                box?.Focus();
                box?.SelectAll();
            },
            DispatcherPriority.Background);
    }

    /// <summary>
    /// Starts renaming a group when the entry of its header menu was chosen.
    /// </summary>
    /// <param name="sender">
    /// The menu item.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnRenameMenuClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: ToggleButton header, DataContext: WatchSectionViewModel section } })
        {
            StartRename(header, section);
        }
    }

    /// <summary>
    /// Commits the new title on Enter and cancels the edit on Esc.
    /// </summary>
    /// <param name="sender">
    /// The rename box.
    /// </param>
    /// <param name="e">
    /// The key event data.
    /// </param>
    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: WatchSectionViewModel section })
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            EndRename(section, commit: true);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            EndRename(section, commit: false);
        }
    }

    /// <summary>
    /// Commits the new title when the rename box loses focus.
    /// </summary>
    /// <param name="sender">
    /// The rename box.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: WatchSectionViewModel section })
        {
            EndRename(section, commit: true);
        }
    }

    /// <summary>
    /// Leaves the in-place title edit and, when asked, saves the typed name. A blank name keeps the old one.
    /// </summary>
    /// <param name="section">
    /// The section being renamed.
    /// </param>
    /// <param name="commit">
    /// Whether to save the typed name.
    /// </param>
    private void EndRename(WatchSectionViewModel section, bool commit)
    {
        // Enter hides the box, which makes it lose focus; the second call must not commit again.
        if (!section.IsEditing)
        {
            return;
        }

        section.IsEditing = false;
        if (commit && DataContext is MainViewModel vm && section.GroupId is { } id)
        {
            _ = vm.RenameGroupAsync(id, section.EditName);
        }
    }

    /// <summary>
    /// Asks for the name of a new group when the entry of the menu on the empty area was chosen.
    /// </summary>
    /// <param name="sender">
    /// The menu item.
    /// </param>
    /// <param name="e">
    /// The event data.
    /// </param>
    private void OnNewGroupFromBlankClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            GroupNamePrompt.Show(this, Loc.Get("Group_NewTitle"), PlacementMode.Pointer, name => vm.CreateGroupAsync(name));
        }
    }

    /// <summary>
    /// Hides the hint about the automatic switch to the custom grouping a few seconds after it appeared.
    /// </summary>
    /// <param name="sender">
    /// The main view model.
    /// </param>
    /// <param name="e">
    /// The property change data.
    /// </param>
    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.GroupingSwitchedFrom) || sender is not MainViewModel { GroupingSwitchedFrom: not null } vm)
        {
            return;
        }

        var shown = vm.GroupingSwitchedFrom;
        DispatcherTimer.RunOnce(
            () =>
            {
                // Dismiss only the hint this timer was started for, not a later one.
                if (vm.GroupingSwitchedFrom == shown)
                {
                    vm.DismissGroupingHint();
                }
            },
            HintDuration);
    }

    /// <summary>
    /// Moves the row that was dragged from one position of the list to another.
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
    private Task MoveRowAsync(int from, int to) =>
        DataContext is MainViewModel vm && from >= 0 && from < vm.Rows.Count
            ? vm.MoveToAsync(vm.Rows[from], to)
            : Task.CompletedTask;

    /// <summary>
    /// Shows a row as lifted while it is dragged, and as resting again afterwards.
    /// </summary>
    /// <param name="container">
    /// The container of the row.
    /// </param>
    /// <param name="lifted">
    /// Whether the row is lifted.
    /// </param>
    private static void LiftRow(Control container, bool lifted)
    {
        if ((container as ContentPresenter)?.Child is UserControl { Content: Control rowBorder } rowView)
        {
            rowView.Classes.Set("dragging", lifted);
            if (lifted)
            {
                ToolTip.SetIsOpen(rowBorder, false);
            }
        }
    }
}

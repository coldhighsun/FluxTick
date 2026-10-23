using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FluxTick.Abstractions;
using FluxTick.App.Localization;

namespace FluxTick.App.ViewModels;

/// <summary>
/// What a section of the card layout collects.
/// </summary>
public enum WatchSectionKind
{
    /// <summary>
    /// Every instrument, shown without a header.
    /// </summary>
    All,

    /// <summary>
    /// The instruments of one asset class.
    /// </summary>
    Type,

    /// <summary>
    /// The instruments of one data source.
    /// </summary>
    Provider,

    /// <summary>
    /// The instruments of one user-defined group.
    /// </summary>
    Group,
}

/// <summary>
/// One collapsible section of the card layout: a header and the cards under it.
/// </summary>
public sealed partial class WatchSectionViewModel : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WatchSectionViewModel"/> class.
    /// </summary>
    /// <param name="kind">
    /// What the section collects.
    /// </param>
    /// <param name="assetClass">
    /// The asset class of a <see cref="WatchSectionKind.Type"/> section.
    /// </param>
    /// <param name="groupId">
    /// The id of the group of a <see cref="WatchSectionKind.Group"/> section.
    /// </param>
    /// <param name="providerId">
    /// The id of the provider of a <see cref="WatchSectionKind.Provider"/> section.
    /// </param>
    public WatchSectionViewModel(WatchSectionKind kind, AssetClass? assetClass = null, string? groupId = null, string? providerId = null)
    {
        Kind = kind;
        AssetClass = assetClass;
        GroupId = groupId;
        ProviderId = providerId;
        Key = KeyOf(kind, assetClass, groupId, providerId);
    }

    /// <summary>
    /// Gets what the section collects.
    /// </summary>
    public WatchSectionKind Kind { get; }

    /// <summary>
    /// Gets the asset class of a <see cref="WatchSectionKind.Type"/> section, otherwise <see langword="null"/>.
    /// </summary>
    public AssetClass? AssetClass { get; }

    /// <summary>
    /// Gets the group id of a <see cref="WatchSectionKind.Group"/> section, otherwise <see langword="null"/>.
    /// </summary>
    public string? GroupId { get; }

    /// <summary>
    /// Gets the provider id of a <see cref="WatchSectionKind.Provider"/> section, otherwise <see langword="null"/>.
    /// </summary>
    public string? ProviderId { get; }

    /// <summary>
    /// Gets the key that identifies the section across rebuilds.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets a value indicating whether the section shows a header that can collapse it.
    /// </summary>
    public bool ShowHeader => Kind != WatchSectionKind.All;

    /// <summary>
    /// Gets the cards of the section in display order.
    /// </summary>
    public ObservableCollection<AssetRowViewModel> Items { get; } = [];

    /// <summary>
    /// Gets or sets the header text.
    /// </summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the title is being edited in place.
    /// </summary>
    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    /// <summary>
    /// Gets or sets the text of the title while it is edited.
    /// </summary>
    [ObservableProperty]
    public partial string EditName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets why the group cannot be deleted right now, or an empty string when it can. Only a group without
    /// instruments can be deleted, and the last group always stays.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    [NotifyPropertyChangedFor(nameof(DeleteMenuHeader))]
    public partial string DeleteBlockedReason { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the section is a user-defined group that can be deleted.
    /// </summary>
    public bool CanDelete => Kind == WatchSectionKind.Group && DeleteBlockedReason.Length == 0;

    /// <summary>
    /// Gets the text of the delete entry in the header menu, which names the reason when the entry is disabled.
    /// </summary>
    public string DeleteMenuHeader => DeleteBlockedReason.Length == 0 ? Loc.Get("Section_DeleteGroup") : Loc.Get("Section_DeleteGroupBlocked", DeleteBlockedReason);

    /// <summary>
    /// Starts editing the title in place. Only user-defined groups can be renamed.
    /// </summary>
    public void BeginRename()
    {
        if (Kind != WatchSectionKind.Group)
        {
            return;
        }

        EditName = Title;
        IsEditing = true;
    }

    /// <summary>
    /// Gets or sets the number of cards in the section.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyHint))]
    public partial int Count { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the cards are hidden and only the header is shown.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyHint))]
    [NotifyPropertyChangedFor(nameof(ShowDropSlot))]
    [NotifyPropertyChangedFor(nameof(ShownItems))]
    public partial bool IsCollapsed { get; set; }

    /// <summary>
    /// Gets the cards the section shows: all of them, or none while it is collapsed. A collapsed section without
    /// items builds no card views, so its cards do not take part in every price update.
    /// </summary>
    public IReadOnlyList<AssetRowViewModel> ShownItems => IsCollapsed ? [] : Items;

    /// <summary>
    /// Gets or sets a value indicating whether the section shows an empty slot as large as the dragged card, so that
    /// an empty group has room for the card while one is dragged over it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyHint))]
    [NotifyPropertyChangedFor(nameof(ShowDropSlot))]
    public partial bool IsDropSlotVisible { get; set; }

    /// <summary>
    /// Gets or sets the width of the drop slot.
    /// </summary>
    [ObservableProperty]
    public partial double DropSlotWidth { get; set; }

    /// <summary>
    /// Gets or sets the height of the drop slot.
    /// </summary>
    [ObservableProperty]
    public partial double DropSlotHeight { get; set; }

    /// <summary>
    /// Gets a value indicating whether a hint is shown because an expanded user-defined group has no cards.
    /// </summary>
    public bool ShowEmptyHint => Kind == WatchSectionKind.Group && Count == 0 && !IsCollapsed && !IsDropSlotVisible;

    /// <summary>
    /// Gets a value indicating whether the drop slot is shown: only while a card is dragged, and only in an
    /// expanded section.
    /// </summary>
    public bool ShowDropSlot => IsDropSlotVisible && !IsCollapsed;

    /// <summary>
    /// Builds the key of a section.
    /// </summary>
    /// <param name="kind">
    /// What the section collects.
    /// </param>
    /// <param name="assetClass">
    /// The asset class of a type section.
    /// </param>
    /// <param name="groupId">
    /// The group id of a group section.
    /// </param>
    /// <param name="providerId">
    /// The provider id of a provider section.
    /// </param>
    /// <returns>
    /// The key.
    /// </returns>
    public static string KeyOf(WatchSectionKind kind, AssetClass? assetClass, string? groupId, string? providerId = null) => kind switch
    {
        WatchSectionKind.Type => $"type:{assetClass}",
        WatchSectionKind.Provider => $"provider:{providerId}",
        WatchSectionKind.Group => $"group:{groupId}",
        _ => "all",
    };
}

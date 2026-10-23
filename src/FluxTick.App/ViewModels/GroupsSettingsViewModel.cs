using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxTick.Core.Application;

namespace FluxTick.App.ViewModels;

/// <summary>
/// One user-defined group in the group editor of the settings page.
/// </summary>
public sealed partial class GroupItemViewModel : ObservableObject
{
    /// <summary>
    /// The editor that owns the item.
    /// </summary>
    private readonly GroupsSettingsViewModel _owner;

    /// <summary>
    /// Suppresses saving while the name is set programmatically.
    /// </summary>
    private bool _loading = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="GroupItemViewModel"/> class.
    /// </summary>
    /// <param name="owner">
    /// The editor that owns the item.
    /// </param>
    /// <param name="id">
    /// The group id.
    /// </param>
    /// <param name="name">
    /// The group name.
    /// </param>
    public GroupItemViewModel(GroupsSettingsViewModel owner, string id, string name)
    {
        _owner = owner;
        Id = id;
        Name = name;
        _loading = false;
    }

    /// <summary>
    /// Gets the group id.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets or sets the group name. A change is saved right away.
    /// </summary>
    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>
    /// Saves a renamed group.
    /// </summary>
    /// <param name="value">
    /// The new name.
    /// </param>
    partial void OnNameChanged(string value)
    {
        if (!_loading)
        {
            _owner.Rename(this);
        }
    }

    /// <summary>
    /// Deletes the group; its instruments move to the first remaining group.
    /// </summary>
    /// <returns>
    /// A task that completes once the change is saved.
    /// </returns>
    [RelayCommand]
    private Task DeleteAsync() => _owner.DeleteAsync(this);
}

/// <summary>
/// The group editor of the settings page: create, rename, reorder and delete user-defined groups. Every change is
/// persisted immediately.
/// </summary>
public sealed partial class GroupsSettingsViewModel : ObservableObject
{
    /// <summary>
    /// The settings store.
    /// </summary>
    private readonly SettingsManager _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="GroupsSettingsViewModel"/> class.
    /// </summary>
    /// <param name="settings">
    /// The settings store.
    /// </param>
    public GroupsSettingsViewModel(SettingsManager settings)
    {
        _settings = settings;
        Reload();
    }

    /// <summary>
    /// Gets the groups in display order.
    /// </summary>
    public ObservableCollection<GroupItemViewModel> Items { get; } = [];

    /// <summary>
    /// Reloads the list from the persisted settings.
    /// </summary>
    public void Reload()
    {
        Items.Clear();
        foreach (var group in _settings.Current.Groups)
        {
            Items.Add(new GroupItemViewModel(this, group.Id, group.Name));
        }
    }

    /// <summary>
    /// Adds a group at the end of the list. A blank name adds nothing.
    /// </summary>
    /// <param name="typedName">
    /// The name as typed.
    /// </param>
    /// <returns>
    /// A task that completes once the group is saved.
    /// </returns>
    public Task AddAsync(string? typedName)
    {
        if (!WatchGroup.TryNormalizeName(typedName, out var name))
        {
            return Task.CompletedTask;
        }

        // The list is updated here and not reloaded: the update may still be waiting for a save in progress, in which
        // case the settings do not hold the group yet.
        var group = new WatchGroup { Name = name };
        var task = _settings.UpdateAsync(s => s.Groups.Add(group));
        Items.Add(new GroupItemViewModel(this, group.Id, group.Name));
        return task;
    }

    /// <summary>
    /// Saves the new name of a group. A blank name is rejected and the previous one is restored.
    /// </summary>
    /// <param name="item">
    /// The renamed item.
    /// </param>
    public void Rename(GroupItemViewModel item)
    {
        if (!WatchGroup.TryNormalizeName(item.Name, out var name))
        {
            Reload();
            return;
        }

        _ = _settings.UpdateAsync(s => s.Groups.Find(g => g.Id == item.Id)?.Name = name);
    }

    /// <summary>
    /// Deletes a group; its instruments move to the first remaining group. Deleting the last group creates a new
    /// default one for them.
    /// </summary>
    /// <param name="item">
    /// The group to delete.
    /// </param>
    /// <returns>
    /// A task that completes once the change is saved.
    /// </returns>
    public async Task DeleteAsync(GroupItemViewModel item)
    {
        var task = _settings.UpdateAsync(s =>
        {
            s.Groups.RemoveAll(g => g.Id == item.Id);
            foreach (var entry in s.Watchlist.Where(e => e.GroupId == item.Id))
            {
                entry.GroupId = null;
            }
        });
        Items.Remove(item);
        await task;

        // Deleting the last group makes the settings create a default one, which only they know about.
        if (Items.Count == 0)
        {
            Reload();
        }
    }

    /// <summary>
    /// Moves a group to a position in the list, for example after it was dragged there. The list shows the new order
    /// right away.
    /// </summary>
    /// <param name="item">
    /// The group to move.
    /// </param>
    /// <param name="newIndex">
    /// The zero-based target position; values outside the list are clamped to its ends.
    /// </param>
    /// <returns>
    /// A task that completes once the change is saved.
    /// </returns>
    public Task MoveToAsync(GroupItemViewModel item, int newIndex)
    {
        var current = Items.IndexOf(item);
        var target = Math.Clamp(newIndex, 0, Items.Count - 1);
        if (current < 0 || current == target)
        {
            return Task.CompletedTask;
        }

        Items.Move(current, target);
        return _settings.UpdateAsync(s => WatchlistReorder.MoveGroup(s.Groups, item.Id, target));
    }
}

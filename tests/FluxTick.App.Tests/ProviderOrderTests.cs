using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the order of the data sources on the settings page.
/// </summary>
public class ProviderOrderTests
{
    /// <summary>
    /// Verifies dragging a data source reorders the list at once and saves the order.
    /// </summary>
    [Fact]
    public async Task MoveProviderAsync_ReordersListAndPersists()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();
        var before = h.SettingsPage.Providers.Select(p => p.Descriptor.Id).ToList();
        before.Count.Should().BeGreaterThan(1);

        await h.SettingsPage.MoveProviderAsync(before.Count - 1, 0);

        var expected = new[] { before[^1] }.Concat(before.Take(before.Count - 1)).ToList();
        h.SettingsPage.Providers.Select(p => p.Descriptor.Id).Should().Equal(expected);
        h.Store.Stored!.ProviderOrder.Should().Equal(expected);
    }

    /// <summary>
    /// Verifies the saved order is applied when the page loads the settings.
    /// </summary>
    [Fact]
    public async Task Reload_AppliesSavedOrder()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();
        var ids = h.SettingsPage.Providers.Select(p => p.Descriptor.Id).Reverse().ToList();

        await h.Settings.UpdateAsync(s => s.ProviderOrder = ids);
        h.SettingsPage.Reload();

        h.SettingsPage.Providers.Select(p => p.Descriptor.Id).Should().Equal(ids);
    }

    /// <summary>
    /// Verifies providers missing from the saved order follow those in it, and unknown ids are ignored.
    /// </summary>
    [Fact]
    public void Sort_PutsUnsavedAfterSavedAndIgnoresUnknown()
    {
        ProviderOrdering.Sort(["a", "b", "c", "d"], ["c", "gone", "a"]).Should().Equal("c", "a", "b", "d");
    }
}

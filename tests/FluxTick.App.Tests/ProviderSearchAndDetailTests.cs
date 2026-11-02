using System.Collections.Specialized;
using FluxTick.Abstractions;
using FluxTick.App.Localization;
using FluxTick.App.ViewModels;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the search, the state filter, the groups and the detail page of the data source list of the settings page.
/// </summary>
public class ProviderSearchAndDetailTests
{
    /// <summary>
    /// Verifies the search finds a source by its id and leaves the others out, and that clearing it lists every source.
    /// </summary>
    [Fact]
    public async Task ProviderSearchText_MatchingId_ListsOnlyMatchingSources()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();

        h.SettingsPage.ProviderSearchText = Harness.Stocks;

        var ids = h.SettingsPage.FilteredProviders.Select(p => p.Descriptor.Id).ToList();
        ids.Should().Contain(Harness.Stocks);
        ids.Should().NotContain(Harness.Crypto);
        h.SettingsPage.ProviderSearchText = string.Empty;
        h.SettingsPage.FilteredProviders.Should().HaveCount(h.SettingsPage.Providers.Count);
    }

    /// <summary>
    /// Verifies the search also matches the name of a supported asset class.
    /// </summary>
    [Fact]
    public async Task ProviderSearchText_AssetClassName_ListsSourcesThatSupportIt()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();

        h.SettingsPage.ProviderSearchText = EnumLabel.Of(AssetClass.Crypto);

        h.SettingsPage.FilteredProviders.Select(p => p.Descriptor.Id).Should().Contain(Harness.Crypto);
    }

    /// <summary>
    /// Verifies a search that nothing matches empties every group and says so.
    /// </summary>
    [Fact]
    public async Task ProviderSearchText_NoMatch_EmptiesTheList()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();

        h.SettingsPage.ProviderSearchText = "no such data source";

        h.SettingsPage.FilteredProviders.Should().BeEmpty();
        h.SettingsPage.HasNoProviderMatch.Should().BeTrue();
    }

    /// <summary>
    /// Verifies the list is split into the sources that are on and those that are off, and a source moves between the
    /// groups when it is switched.
    /// </summary>
    [Fact]
    public async Task Groups_SwitchingASource_MovesItBetweenEnabledAndDisabled()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();
        var plain = h.SettingsPage.Providers.Single(p => p.Descriptor.Id == Harness.Plain);

        plain.Enabled = false;

        h.SettingsPage.FilteredProviders.Where(p => !p.Enabled).Should().Equal(plain);
        plain.SectionTitle.Should().Be(Loc.Get("Settings_SourcesDisabled"));
        plain.Enabled = true;
        h.SettingsPage.FilteredProviders.Should().OnlyContain(p => p.Enabled);
        plain.SectionTitle.Should().BeNull();
        h.SettingsPage.FilteredProviders[0].SectionTitle.Should().Be(Loc.Get("Settings_SourcesEnabled"));
    }

    /// <summary>
    /// Verifies the state filter for enabled sources leaves out the sources that are off.
    /// </summary>
    [Fact]
    public async Task ProviderStatusFilter_Enabled_ListsOnlySourcesThatAreOn()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();
        var plain = h.SettingsPage.Providers.Single(p => p.Descriptor.Id == Harness.Plain);
        plain.Enabled = false;

        h.SettingsPage.ProviderStatusFilters.Single(f => f.Filter == ProviderStatusFilter.Enabled).IsSelected = true;

        h.SettingsPage.FilteredProviders.Should().NotContain(plain);
        h.SettingsPage.FilteredProviders.Should().OnlyContain(p => p.Enabled);
    }

    /// <summary>
    /// Verifies the state filter for sources that need attention lists a source that is on but lacks its key.
    /// </summary>
    [Fact]
    public async Task ProviderStatusFilter_NeedsAttention_ListsSourcesLackingRequiredValues()
    {
        var settings = new AppSettings();
        settings.Providers["keyed"] = new ProviderSettings { Enabled = true };
        await using var h = new Harness(settings, extraFactory: new FakeFactory("keyed", [AssetClass.Commodity], requiresKey: true));
        await h.Main.InitializeAsync();

        h.SettingsPage.ProviderStatusFilters.Single(f => f.Filter == ProviderStatusFilter.NeedsAttention).IsSelected = true;

        h.SettingsPage.FilteredProviders.Select(p => p.Descriptor.Id).Should().Equal("keyed");
    }

    /// <summary>
    /// Verifies exactly one state filter is checked, starting with all, and unchecking the checked one keeps it checked.
    /// </summary>
    [Fact]
    public async Task ProviderStatusFilters_StartWithAll_AndKeepOneChecked()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();
        var filters = h.SettingsPage.ProviderStatusFilters;
        filters.Single(f => f.IsSelected).Filter.Should().Be(ProviderStatusFilter.All);
        var enabled = filters.Single(f => f.Filter == ProviderStatusFilter.Enabled);
        enabled.IsSelected = true;

        enabled.IsSelected = false;

        enabled.IsSelected.Should().BeTrue();
        filters.Count(f => f.IsSelected).Should().Be(1);
    }

    /// <summary>
    /// Verifies the summary counts the sources that are on and those that need attention.
    /// </summary>
    [Fact]
    public async Task ProvidersSummary_CountsEnabledAndNeedingAttention()
    {
        var settings = new AppSettings();
        settings.Providers["keyed"] = new ProviderSettings { Enabled = true };
        await using var h = new Harness(settings, extraFactory: new FakeFactory("keyed", [AssetClass.Commodity], requiresKey: true));
        await h.Main.InitializeAsync();
        var page = h.SettingsPage;

        var expected = Loc.Get("Settings_ProvidersSummary", page.Providers.Count(p => p.Enabled), page.Providers.Count);

        page.ProvidersSummary.Should().Be($"{expected} · {Loc.Get("Settings_ProvidersAttention", 1)}");
    }

    /// <summary>
    /// Verifies a source that needs a key is marked so, and a source without one is not.
    /// </summary>
    [Fact]
    public async Task HasMissingRequired_SourceWithMissingRequiredValue_IsMarked()
    {
        await using var h = new Harness(new AppSettings(), extraFactory: new FakeFactory("keyed", [AssetClass.Commodity], requiresKey: true));
        await h.Main.InitializeAsync();

        h.SettingsPage.Providers.Single(p => p.Descriptor.Id == "keyed").HasMissingRequired.Should().BeTrue();
        h.SettingsPage.Providers.Single(p => p.Descriptor.Id == Harness.Plain).HasMissingRequired.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the options are split into the ones in view and the advanced ones, and an error in an advanced option
    /// opens its section.
    /// </summary>
    [Fact]
    public void AdvancedOptions_WithError_OpenTheSection()
    {
        var descriptor = new ProviderDescriptor(
            "demo",
            "Demo",
            "Test",
            [AssetClass.Equity],
            ProviderUpdateMode.Polling,
            Options:
            [
                new ProviderOptionDefinition("Name", "Name", ProviderOptionType.String, "a"),
                new ProviderOptionDefinition("Port", "Port", ProviderOptionType.Int, "5", Min: 1, Max: 9, Advanced: true),
            ]);
        var provider = new ProviderSettingsViewModel(descriptor, _ => { });

        provider.BasicOptions.Select(o => o.Definition.Key).Should().Equal("Name");
        provider.AdvancedOptions.Select(o => o.Definition.Key).Should().Equal("Port");
        provider.HasBasicOptions.Should().BeTrue();
        provider.HasAdvancedOptions.Should().BeTrue();
        provider.IsAdvancedExpanded.Should().BeFalse();

        provider.Enabled = true;
        provider.AdvancedOptions[0].Text = "99";
        provider.SaveCommand.Execute(null);

        provider.HasAdvancedError.Should().BeTrue();
        provider.IsAdvancedExpanded.Should().BeTrue();
    }

    /// <summary>
    /// Verifies a live status change under the needs-attention filter updates the count but leaves the rows alone, and
    /// the next filter change picks the status up.
    /// </summary>
    [Fact]
    public async Task ProviderStatusFilter_NeedsAttention_StatusChangeUpdatesCountWithoutMovingRows()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();
        var page = h.SettingsPage;
        var provider = page.Providers.Single(p => p.Descriptor.Id == Harness.Crypto);
        provider.Enabled = true;
        var attention = page.ProviderStatusFilters.Single(f => f.Filter == ProviderStatusFilter.NeedsAttention);
        attention.IsSelected = true;
        page.FilteredProviders.Should().BeEmpty();

        provider.UpdateStatus(new ProviderStatus(ProviderConnectionState.Failed, "down"));

        page.FilteredProviders.Should().BeEmpty();
        page.ProvidersSummary.Should().Contain(Loc.Get("Settings_ProvidersAttention", 1));
        page.ProviderStatusFilters.Single(f => f.Filter == ProviderStatusFilter.All).IsSelected = true;
        attention.IsSelected = true;
        page.FilteredProviders.Should().Equal(provider);

        provider.UpdateStatus(new ProviderStatus(ProviderConnectionState.Connected));

        page.FilteredProviders.Should().Equal(provider);
        page.ProvidersSummary.Should().NotContain(Loc.Get("Settings_ProvidersAttention", 1));
    }

    /// <summary>
    /// Verifies a status change leaves the list alone unless the state filter depends on it, while the count above the
    /// list is still updated.
    /// </summary>
    [Fact]
    public async Task ProviderStatus_ChangeWithAllShown_DoesNotRebuildTheListButUpdatesTheSummary()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();
        var page = h.SettingsPage;
        var provider = page.Providers.Single(p => p.Descriptor.Id == Harness.Crypto);
        provider.Enabled = true;
        var moves = 0;
        var summaries = 0;
        page.FilteredProviders.CollectionChanged += (_, _) => moves++;
        page.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.ProvidersSummary))
            {
                summaries++;
            }
        };

        provider.UpdateStatus(new ProviderStatus(ProviderConnectionState.Failed, "down"));

        moves.Should().Be(0);
        summaries.Should().BePositive();
    }

    /// <summary>
    /// Verifies the search text is matched case-insensitively against the name, and a blank text matches every source.
    /// </summary>
    [Fact]
    public async Task Matches_NameInOtherCase_IsFound()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();
        var provider = h.SettingsPage.Providers.First();

        provider.Matches(provider.Name.ToUpperInvariant()).Should().BeTrue();
        provider.Matches("  ").Should().BeTrue();
        provider.Matches(null).Should().BeTrue();
    }

    /// <summary>
    /// Verifies reloading the page closes a detail page that has no unsaved edits.
    /// </summary>
    [Fact]
    public async Task Reload_WithDetailOpen_ShowsTheListAgain()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();
        var page = h.SettingsPage;
        page.Providers[0].Open();
        page.IsProviderDetailOpen.Should().BeTrue();

        page.Reload();

        page.SelectedProvider.Should().BeNull();
        page.IsProviderDetailOpen.Should().BeFalse();
    }

    /// <summary>
    /// Verifies reloading the page keeps the detail page open on a source with unsaved edits, as the edits are kept.
    /// </summary>
    [Fact]
    public async Task Reload_WithDetailOpenOnUnsavedEdit_KeepsTheDetailPage()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();
        var page = h.SettingsPage;
        var provider = page.Providers.First(p => p.Options.Count > 0);
        provider.Open();
        provider.Options[0].Text = "edited";
        provider.IsDirty.Should().BeTrue();

        page.Reload();

        page.SelectedProvider.Should().BeSameAs(provider);
        provider.IsDirty.Should().BeTrue();
    }

    /// <summary>
    /// Verifies reloading with a detail page kept open for unsaved edits also keeps the search and the state filter.
    /// </summary>
    [Fact]
    public async Task Reload_WithDetailKeptForUnsavedEdit_KeepsSearchAndStateFilter()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();
        var page = h.SettingsPage;
        var provider = page.Providers.First(p => p.Options.Count > 0);
        page.ProviderSearchText = "a";
        page.ProviderStatusFilters.Single(f => f.Filter == ProviderStatusFilter.Enabled).IsSelected = true;
        provider.Open();
        provider.Options[0].Text = "edited";

        page.Reload();

        page.ProviderSearchText.Should().Be("a");
        page.ProviderStatusFilters.Single(f => f.IsSelected).Filter.Should().Be(ProviderStatusFilter.Enabled);
    }

    /// <summary>
    /// Verifies a source that is switched keeps its place in the one collection (it is moved, not removed and added),
    /// so its row keeps the keyboard focus.
    /// </summary>
    [Fact]
    public async Task Groups_SwitchingASource_MovesItInsteadOfRecreatingIt()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();
        var page = h.SettingsPage;
        var provider = page.FilteredProviders[0];
        var actions = new List<NotifyCollectionChangedAction>();
        page.FilteredProviders.CollectionChanged += (_, e) => actions.Add(e.Action);

        provider.Enabled = false;

        actions.Should().OnlyContain(a => a == NotifyCollectionChangedAction.Move);
        page.FilteredProviders[^1].Should().BeSameAs(provider);
    }

    /// <summary>
    /// Verifies reloading the page clears the search and puts the state filter back on all, and keeps the asset class filter.
    /// </summary>
    [Fact]
    public async Task Reload_AfterSearchAndStateFilter_StartsOver()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();
        var page = h.SettingsPage;
        page.ProviderSearchText = "no such data source";
        page.ProviderStatusFilters.Single(f => f.Filter == ProviderStatusFilter.NeedsAttention).IsSelected = true;
        page.ProviderClassFilters.Single(f => f.AssetClass == AssetClass.Equity).IsSelected = true;

        page.Reload();

        page.ProviderSearchText.Should().BeEmpty();
        page.ProviderStatusFilters.Single(f => f.IsSelected).Filter.Should().Be(ProviderStatusFilter.All);
        page.ProviderClassFilters.Single(f => f.IsSelected).AssetClass.Should().Be(AssetClass.Equity);
        page.FilteredProviders.Should().OnlyContain(p => p.Descriptor.Supports(AssetClass.Equity));
        page.FilteredProviders.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies a source whose switch is refused for a missing key never joins the list of sources that need attention,
    /// and opens its detail page once the switch is back.
    /// </summary>
    [Fact]
    public async Task ProviderStatusFilter_NeedsAttention_RefusedSwitchNeverJoinsTheList()
    {
        await using var h = new Harness(new AppSettings(), extraFactory: new FakeFactory("keyed", [AssetClass.Commodity], requiresKey: true));
        await h.Main.InitializeAsync();
        var page = h.SettingsPage;
        var keyed = page.Providers.Single(p => p.Descriptor.Id == "keyed");
        page.ProviderStatusFilters.Single(f => f.Filter == ProviderStatusFilter.NeedsAttention).IsSelected = true;
        page.FilteredProviders.Should().BeEmpty();
        var changes = 0;
        page.FilteredProviders.CollectionChanged += (_, _) => changes++;

        keyed.Enabled = true;

        changes.Should().Be(0);
        keyed.Enabled.Should().BeFalse();
        page.FilteredProviders.Should().BeEmpty();
        page.SelectedProvider.Should().BeSameAs(keyed);
    }
}

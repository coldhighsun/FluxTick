using System.Collections.Specialized;
using FluxTick.Abstractions;
using FluxTick.App.Localization;
using FluxTick.App.ViewModels;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the data source list of the settings page: its order and the asset class filter.
/// </summary>
public class ProviderListTests
{
    /// <summary>
    /// Verifies the list is in ascending order of the displayed name.
    /// </summary>
    [Fact]
    public async Task FilteredProviders_ByDefault_ListsAllInOrderOfName()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();

        var names = h.SettingsPage.FilteredProviders.Select(p => p.Name).ToList();

        names.Should().HaveCount(h.SettingsPage.Providers.Count);
        names.Should().BeInAscendingOrder(StringComparer.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// Verifies the disabled sources come after the enabled ones, and move when a source is switched.
    /// </summary>
    [Fact]
    public async Task FilteredProviders_WithDisabledSource_ListsItLast()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();
        var first = h.SettingsPage.FilteredProviders[0];

        first.Enabled = false;

        h.SettingsPage.FilteredProviders[^1].Should().BeSameAs(first);
        first.Enabled = true;
        h.SettingsPage.FilteredProviders[0].Should().BeSameAs(first);
    }

    /// <summary>
    /// Verifies switching one source does not reset the collection, so the other cards keep their controls.
    /// </summary>
    [Fact]
    public async Task FilteredProviders_WhenSourceIsSwitched_DoesNotResetTheCollection()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();
        var resets = 0;
        h.SettingsPage.FilteredProviders.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                resets++;
            }
        };

        h.SettingsPage.FilteredProviders[0].Enabled = false;

        resets.Should().Be(0);
        h.SettingsPage.FilteredProviders[^1].Enabled.Should().BeFalse();
    }

    /// <summary>
    /// Verifies switching a source on does not expand a card that has no required value missing.
    /// </summary>
    [Fact]
    public async Task Enabled_SwitchedOn_DoesNotExpandTheCard()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();
        var provider = h.SettingsPage.Providers.First(p => p.HasOptions);
        provider.Enabled = false;

        provider.Enabled = true;

        provider.IsExpanded.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a disabled source stays behind the enabled ones within a filtered class, and the filter survives a reload.
    /// </summary>
    [Fact]
    public async Task Reload_WithFilterAndDisabledSource_KeepsTheFilterAndTheOrder()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();
        h.SettingsPage.ProviderClassFilters.Single(f => f.AssetClass == AssetClass.Equity).IsSelected = true;
        h.SettingsPage.FilteredProviders.Single(p => p.Descriptor.Id == Harness.Plain).Enabled = false;

        h.SettingsPage.Reload();

        h.SettingsPage.FilteredProviders.Select(p => p.Descriptor.Id).Should().Equal(Harness.Stocks, Harness.Plain);
    }

    /// <summary>
    /// Verifies picking an asset class lists only the sources that support it, and picking all lists every source again.
    /// </summary>
    [Fact]
    public async Task SelectingAssetClass_ListsOnlySupportingSources()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();

        h.SettingsPage.ProviderClassFilters.Single(f => f.AssetClass == AssetClass.Equity).IsSelected = true;

        h.SettingsPage.FilteredProviders.Select(p => p.Descriptor.Id).Should().BeEquivalentTo(Harness.Stocks, Harness.Plain);

        h.SettingsPage.ProviderClassFilters.Single(f => f.AssetClass == AssetClass.Crypto).IsSelected = true;

        h.SettingsPage.FilteredProviders.Select(p => p.Descriptor.Id).Should().Equal(Harness.Crypto);

        h.SettingsPage.ProviderClassFilters.Single(f => f.AssetClass is null).IsSelected = true;

        h.SettingsPage.FilteredProviders.Should().HaveCount(h.SettingsPage.Providers.Count);
    }

    /// <summary>
    /// Verifies exactly one choice is selected, starting with all, and that only asset classes some source supports are offered.
    /// </summary>
    [Fact]
    public async Task ProviderClassFilters_OfferAllAndSupportedClasses_WithOneSelected()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();
        var filters = h.SettingsPage.ProviderClassFilters;

        filters.Select(f => f.AssetClass).Should().Equal(null, AssetClass.Crypto, AssetClass.Equity, AssetClass.Index);
        filters.Single(f => f.IsSelected).AssetClass.Should().BeNull();

        filters.Single(f => f.AssetClass == AssetClass.Index).IsSelected = true;

        filters.Single(f => f.IsSelected).AssetClass.Should().Be(AssetClass.Index);
    }

    /// <summary>
    /// Verifies unchecking the checked choice of the asset class filter keeps it checked.
    /// </summary>
    [Fact]
    public async Task ProviderClassFilter_UncheckingTheCheckedChoice_KeepsItChecked()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();
        var crypto = h.SettingsPage.ProviderClassFilters.Single(f => f.AssetClass == AssetClass.Crypto);
        crypto.IsSelected = true;

        crypto.IsSelected = false;

        crypto.IsSelected.Should().BeTrue();
        h.SettingsPage.ProviderClassFilters.Count(f => f.IsSelected).Should().Be(1);
        h.SettingsPage.FilteredProviders.Select(p => p.Descriptor.Id).Should().Equal(Harness.Crypto);
    }

    /// <summary>
    /// Verifies the choices are labelled in the language in use.
    /// </summary>
    [Fact]
    public async Task ProviderClassFilter_Label_UsesTheCurrentLanguage()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();
        var all = h.SettingsPage.ProviderClassFilters.Single(f => f.AssetClass is null);

        all.Label.Should().Be(Loc.Get("Settings_FilterAll"));
        h.SettingsPage.ProviderClassFilters.Single(f => f.AssetClass == AssetClass.Equity).Label
            .Should().Be(EnumLabel.Of(AssetClass.Equity));
    }

    /// <summary>
    /// Verifies a source without required values does not open when it is switched on.
    /// </summary>
    [Fact]
    public async Task Enabled_SwitchedOnWithoutRequiredOptions_StaysCollapsed()
    {
        await using var h = CreateWithKeyedSource();
        await h.Main.InitializeAsync();
        var plain = h.SettingsPage.Providers.Single(p => p.Descriptor.Id == Harness.Crypto);
        plain.Enabled = false;

        plain.Enabled = true;

        plain.IsExpanded.Should().BeFalse();
        plain.HasOptionError.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a source that is enabled in the settings but lacks a required value is opened and flagged when the page loads.
    /// </summary>
    [Fact]
    public async Task Reload_EnabledSourceLacksRequiredValue_IsFlaggedAndOpened()
    {
        var settings = new AppSettings();
        settings.Providers["keyed"] = new ProviderSettings { Enabled = true };
        await using var h = new Harness(settings, extraFactory: new FakeFactory("keyed", [AssetClass.Commodity], requiresKey: true));
        await h.Main.InitializeAsync();

        var keyed = h.SettingsPage.Providers.Single(p => p.Descriptor.Id == "keyed");

        keyed.IsExpanded.Should().BeTrue();
        keyed.HasOptionError.Should().BeTrue();
        keyed.StatusText.Should().Be(Loc.Get("Provider_NeedsSetup"));
        keyed.StatusBad.Should().BeTrue();
    }

    /// <summary>
    /// Creates the harness with a source that needs an API key and is off at first.
    /// </summary>
    /// <returns>
    /// The harness.
    /// </returns>
    private static Harness CreateWithKeyedSource() =>
        new(new AppSettings(), extraFactory: new FakeFactory("keyed", [AssetClass.Commodity], requiresKey: true));

    /// <summary>
    /// Verifies a source whose required value is not saved can not be switched on: the switch goes back, the card opens on
    /// the missing field, and nothing is written.
    /// </summary>
    [Fact]
    public async Task Enabled_SwitchedOnWithRequiredValueMissing_GoesBackAndOpensTheCard()
    {
        await using var h = CreateWithKeyedSource();
        await h.Main.InitializeAsync();
        var keyed = h.SettingsPage.Providers.Single(p => p.Descriptor.Id == "keyed");
        keyed.IsExpanded.Should().BeFalse();

        keyed.Enabled = true;

        keyed.Enabled.Should().BeFalse();
        keyed.IsExpanded.Should().BeTrue();
        keyed.HasOptionError.Should().BeFalse("a source that is off is not flagged");
        var key = keyed.Options.Single(o => o.Definition.Key == "ApiKey");
        key.Error.Should().NotBeEmpty();
        key.Label.Should().EndWith("*");
        h.Store.Stored!.Providers.ContainsKey("keyed").Should().BeFalse();
    }

    /// <summary>
    /// Verifies a source can be switched on once its required value is saved, and the key is stored with it.
    /// </summary>
    [Fact]
    public async Task Enabled_AfterTheRequiredValueIsSaved_StaysOn()
    {
        await using var h = CreateWithKeyedSource();
        await h.Main.InitializeAsync();
        var keyed = h.SettingsPage.Providers.Single(p => p.Descriptor.Id == "keyed");
        var key = keyed.Options.Single(o => o.Definition.Key == "ApiKey");
        keyed.Enabled = true;

        key.Text = "secret";
        keyed.Enabled = true;

        keyed.Enabled.Should().BeFalse("the key is typed but not saved yet");
        keyed.SaveCommand.Execute(null);
        keyed.Enabled = true;

        keyed.Enabled.Should().BeTrue();
        await Harness.EventuallyAsync(() => h.Store.Stored!.Providers.TryGetValue("keyed", out var saved) && saved.Enabled);
        h.Store.Stored!.Providers["keyed"].Options["ApiKey"].Should().Be("secret");
        key.Error.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies a source that is off may be saved with a blank required value, while one that is on may not.
    /// </summary>
    [Fact]
    public async Task Save_RequiredValueBlank_IsRefusedOnlyWhileTheSourceIsOn()
    {
        await using var h = CreateWithKeyedSource();
        await h.Main.InitializeAsync();
        var keyed = h.SettingsPage.Providers.Single(p => p.Descriptor.Id == "keyed");
        var key = keyed.Options.Single(o => o.Definition.Key == "ApiKey");
        key.Text = "secret";
        keyed.SaveCommand.Execute(null);
        keyed.Enabled = true;
        await Harness.EventuallyAsync(() => h.Store.Stored!.Providers["keyed"].Enabled);

        key.Text = string.Empty;
        keyed.Options.Single(o => o.Definition.Key == "Url").Text = "https://b";
        keyed.SaveCommand.Execute(null);

        key.Error.Should().NotBeEmpty();
        h.Store.Stored!.Providers["keyed"].Options["ApiKey"].Should().Be("secret");
        h.Store.Stored.Providers["keyed"].Options.ContainsKey("Url").Should().BeFalse();

        keyed.Enabled = false;
        keyed.SaveCommand.Execute(null);

        key.Error.Should().BeEmpty();
        await Harness.EventuallyAsync(() => h.Store.Stored!.Providers["keyed"].Options.GetValueOrDefault("Url") == "https://b");
        h.Store.Stored!.Providers["keyed"].Options.ContainsKey("ApiKey").Should().BeFalse();
    }

    /// <summary>
    /// Verifies the switch saves at once with the saved options only, never with an unsaved draft.
    /// </summary>
    [Fact]
    public async Task Enabled_Toggled_SavesAtOnceWithoutTheDraft()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();
        var provider = h.SettingsPage.Providers.Single(p => p.Descriptor.Id == Harness.Crypto);
        provider.Options.Single(o => o.Definition.Key == "Url").Text = "https://b";

        provider.Enabled = false;

        await Harness.EventuallyAsync(() => h.Store.Stored!.Providers.TryGetValue(Harness.Crypto, out var saved) && !saved.Enabled);
        h.Store.Stored!.Providers[Harness.Crypto].Options.Should().BeEmpty();
        provider.IsDirty.Should().BeTrue();
    }

    /// <summary>
    /// Verifies reloading the page, as happens when the window is opened again, keeps unsaved edits.
    /// </summary>
    [Fact]
    public async Task Reload_KeepsUnsavedEdits()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();
        var provider = h.SettingsPage.Providers.Single(p => p.Descriptor.Id == Harness.Crypto);
        var url = provider.Options.Single(o => o.Definition.Key == "Url");
        url.Text = "https://b";
        provider.IsDirty.Should().BeTrue();

        h.SettingsPage.Reload();

        url.Text.Should().Be("https://b");
        provider.IsDirty.Should().BeTrue();
    }

    /// <summary>
    /// Verifies a source that is on without its required value says that it needs setup, and shows its status again once
    /// the value is saved.
    /// </summary>
    [Fact]
    public async Task Status_EnabledWithRequiredValueMissing_SaysNeedsSetupUntilSaved()
    {
        var settings = new AppSettings();
        settings.Providers["keyed"] = new ProviderSettings { Enabled = true };
        await using var h = new Harness(settings, extraFactory: new FakeFactory("keyed", [AssetClass.Commodity], requiresKey: true));
        await h.Main.InitializeAsync();
        var keyed = h.SettingsPage.Providers.Single(p => p.Descriptor.Id == "keyed");
        keyed.StatusText.Should().Be(Loc.Get("Provider_NeedsSetup"));

        keyed.Options.Single(o => o.Definition.Key == "ApiKey").Text = "secret";
        keyed.StatusText.Should().Be(Loc.Get("Provider_NeedsSetup"), "the key is not saved yet");
        keyed.SaveCommand.Execute(null);

        keyed.StatusText.Should().NotBe(Loc.Get("Provider_NeedsSetup"));
        keyed.HasOptionError.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the switch is put back after the input that is being handled, not inside it.
    /// </summary>
    [Fact]
    public void Enabled_SwitchedOnWithRequiredValueMissing_GoesBackLater()
    {
        var descriptor = new ProviderDescriptor(
            "keyed",
            "Keyed",
            "Test",
            [AssetClass.Commodity],
            ProviderUpdateMode.Streaming,
            Options: [new ProviderOptionDefinition("ApiKey", "API key", ProviderOptionType.Secret, string.Empty, Required: true)],
            EnabledByDefault: false);
        var later = new List<Action>();
        var provider = new ProviderSettingsViewModel(descriptor, _ => { }, later.Add);

        provider.Enabled = true;

        provider.Enabled.Should().BeTrue("the switch is still part of the binding update");
        later.Should().ContainSingle();

        later[0]();

        provider.Enabled.Should().BeFalse();
    }

    /// <summary>
    /// Verifies saving a card that has no edit does nothing, so a provider is not restarted for no change.
    /// </summary>
    [Fact]
    public void Save_WithoutEdits_DoesNothing()
    {
        var descriptor = new ProviderDescriptor(
            "plain",
            "Plain",
            "Test",
            [AssetClass.Crypto],
            ProviderUpdateMode.Streaming,
            Options: [new ProviderOptionDefinition("Url", "Url", ProviderOptionType.String, "https://a")]);
        var saves = 0;
        var provider = new ProviderSettingsViewModel(descriptor, _ => saves++);

        provider.SaveCommand.Execute(null);

        saves.Should().Be(0);
    }

    /// <summary>
    /// Verifies the card is told about unsaved edits when the state changes, not on every keystroke.
    /// </summary>
    [Fact]
    public void IsDirty_IsAnnouncedOnlyWhenItChanges()
    {
        var descriptor = new ProviderDescriptor(
            "plain",
            "Plain",
            "Test",
            [AssetClass.Crypto],
            ProviderUpdateMode.Streaming,
            Options: [new ProviderOptionDefinition("Url", "Url", ProviderOptionType.String, "https://a")]);
        var provider = new ProviderSettingsViewModel(descriptor, _ => { });
        var announced = 0;
        provider.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProviderSettingsViewModel.IsDirty))
            {
                announced++;
            }
        };

        provider.Options.Single().Text = "https://b";
        provider.Options.Single().Text = "https://bc";
        provider.Options.Single().Text = "https://bcd";

        announced.Should().Be(1);
    }

    /// <summary>
    /// Verifies a refused enable does not move the card in the list.
    /// </summary>
    [Fact]
    public async Task Enabled_SwitchedOnWithRequiredValueMissing_DoesNotMoveTheCard()
    {
        await using var h = CreateWithKeyedSource();
        await h.Main.InitializeAsync();
        var keyed = h.SettingsPage.Providers.Single(p => p.Descriptor.Id == "keyed");
        var changes = 0;
        h.SettingsPage.FilteredProviders.CollectionChanged += (_, _) => changes++;

        keyed.Enabled = true;

        changes.Should().Be(0);
        keyed.Enabled.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a draft survives the page being loaded again, and is measured against the settings as they are then.
    /// </summary>
    [Fact]
    public async Task Reload_WithADraft_KeepsItAndFollowsTheSavedValue()
    {
        await using var h = new Harness(new AppSettings());
        await h.Main.InitializeAsync();
        var provider = h.SettingsPage.Providers.Single(p => p.Descriptor.Id == Harness.Crypto);
        var url = provider.Options.Single(o => o.Definition.Key == "Url");
        url.Text = "https://draft";
        await h.Settings.UpdateAsync(s => s.Providers[Harness.Crypto] = new ProviderSettings { Options = { ["Url"] = "https://saved" } });

        h.SettingsPage.Reload();

        url.Text.Should().Be("https://draft");
        provider.IsDirty.Should().BeTrue();

        provider.RevertCommand.Execute(null);

        url.Text.Should().Be("https://saved");
    }

    /// <summary>
    /// Verifies the card links to the site of the data source and opens it when asked.
    /// </summary>
    [Fact]
    public void OpenHomepage_DataSourceWithASite_OpensIt()
    {
        var opened = new List<string>();
        var descriptor = new ProviderDescriptor(
            "site",
            "Site",
            "Test",
            [AssetClass.Equity],
            ProviderUpdateMode.Streaming,
            Homepage: new Uri("https://www.example.test/markets/"));
        var provider = new ProviderSettingsViewModel(descriptor, _ => { }, openWebsite: url =>
        {
            opened.Add(url);
            return true;
        });

        provider.HasHomepage.Should().BeTrue();
        provider.HomepageHost.Should().Be("example.test", "the www. in front is not worth showing");
        provider.OpenHomepageCommand.Execute(null);

        opened.Should().Equal("https://www.example.test/markets/");
    }

    /// <summary>
    /// Verifies a data source without a site, or with an address the app does not open, shows no link and opens nothing.
    /// </summary>
    /// <param name="homepage">
    /// The address, or <see langword="null"/>.
    /// </param>
    [Theory]
    [InlineData(null)]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ftp://example.test/")]
    [InlineData("http://example.test/")]
    [InlineData("/docs/markets")]
    public void OpenHomepage_NoSiteOrUnsafeAddress_ShowsNoLinkAndOpensNothing(string? homepage)
    {
        var opened = new List<string>();
        var descriptor = new ProviderDescriptor(
            "site",
            "Site",
            "Test",
            [AssetClass.Equity],
            ProviderUpdateMode.Streaming,
            Homepage: homepage is null ? null : new Uri(homepage, UriKind.RelativeOrAbsolute));
        var provider = new ProviderSettingsViewModel(descriptor, _ => { }, openWebsite: url =>
        {
            opened.Add(url);
            return true;
        });

        provider.HasHomepage.Should().BeFalse();
        provider.HomepageHost.Should().BeEmpty();
        provider.OpenHomepageCommand.Execute(null);

        opened.Should().BeEmpty();
    }
}

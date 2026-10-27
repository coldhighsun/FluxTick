using FluxTick.Abstractions;
using FluxTick.App.Localization;
using FluxTick.App.ViewModels;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the main, add-instrument and settings view models working together.
/// </summary>
public class ViewModelFlowTests
{
    /// <summary>
    /// The Bitcoin test asset.
    /// </summary>
    private static readonly Asset Btc = new(Harness.Crypto, "BTC", "Bitcoin", AssetClass.Crypto);

    /// <summary>
    /// The Apple test asset.
    /// </summary>
    private static readonly Asset Aapl = new(Harness.Stocks, "AAPL", "Apple", AssetClass.Equity);

    /// <summary>
    /// The Ether test asset.
    /// </summary>
    private static readonly Asset Eth = new(Harness.Crypto, "ETH", "Ether", AssetClass.Crypto);

    /// <summary>
    /// Verifies rows are built from the persisted watchlist in order.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_BuildsRowsInWatchlistOrder()
    {
        await using var h = new Harness(Harness.WatchlistOf(Aapl, Btc));

        await h.Main.InitializeAsync();

        h.Main.Rows.Select(r => r.Symbol).Should().Equal("AAPL", "BTC");
        h.Main.IsEmpty.Should().BeFalse();
        h.Main.Chips.Select(c => c.Name).Should().BeEquivalentTo(["STOCKS", "CRYPTO"]);
    }

    /// <summary>
    /// Verifies a full screen window shows the cards, without touching the layout setting, and the list comes back
    /// afterwards.
    /// </summary>
    [Fact]
    public async Task SetFullScreen_ListLayout_ShowsCardsTemporarily()
    {
        await using var h = new Harness(Harness.WatchlistOf(Aapl, Btc));
        await h.Main.InitializeAsync();
        h.Main.IsCardLayout.Should().BeFalse();

        var tipBefore = h.Main.FullScreenTip;

        h.Main.SetFullScreen(true);

        h.Main.IsFullScreen.Should().BeTrue();
        h.Main.FullScreenTip.Should().NotBe(tipBefore);
        h.Main.IsCardLayout.Should().BeTrue();
        h.Settings.Current.Appearance.Layout.Should().Be(WatchlistLayout.List);

        h.Main.SetFullScreen(false);

        h.Main.IsFullScreen.Should().BeFalse();
        h.Main.FullScreenTip.Should().Be(tipBefore);
        h.Main.IsCardLayout.Should().BeFalse();
    }

    /// <summary>
    /// Verifies choosing the list layout while the window is full screen asks it to leave full screen, with the saved list
    /// layout, and that the other layout choices do not.
    /// </summary>
    [Fact]
    public async Task ChooseListWhileFullScreen_AsksToLeaveFullScreen()
    {
        var settings = Harness.WatchlistOf(Aapl, Btc);
        settings.Appearance.Layout = WatchlistLayout.Cards;
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();
        var requests = 0;
        h.Main.FullScreenExitRequested += (_, _) => requests++;

        // Not full screen: nothing to leave. Choosing the cards while full screen does not leave either.
        await h.Main.SetLayoutCommand.ExecuteAsync(WatchlistLayout.List);
        requests.Should().Be(0);
        h.Main.SetFullScreen(true);
        await h.Main.SetLayoutCommand.ExecuteAsync(WatchlistLayout.Cards);
        requests.Should().Be(0);

        await h.Main.SetLayoutCommand.ExecuteAsync(WatchlistLayout.List);

        requests.Should().Be(1);
        h.Settings.Current.Appearance.Layout.Should().Be(WatchlistLayout.List);
        h.Main.SetFullScreen(false);
        h.Main.IsCardLayout.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the layout button of the header goes to the list while the window is full screen, even though the saved
    /// layout is the list already and the cards are only shown because of the full screen.
    /// </summary>
    [Fact]
    public async Task ToggleLayoutWhileFullScreen_GoesToTheListAndLeavesFullScreen()
    {
        await using var h = new Harness(Harness.WatchlistOf(Aapl, Btc));
        await h.Main.InitializeAsync();
        var requests = 0;
        h.Main.FullScreenExitRequested += (_, _) => requests++;
        h.Main.SetFullScreen(true);

        await h.Main.ToggleLayoutCommand.ExecuteAsync(null);

        requests.Should().Be(1);
        h.Settings.Current.Appearance.Layout.Should().Be(WatchlistLayout.List);
    }

    /// <summary>
    /// Verifies a window that already shows the cards keeps them after full screen.
    /// </summary>
    [Fact]
    public async Task SetFullScreen_CardLayout_KeepsCards()
    {
        var settings = Harness.WatchlistOf(Aapl, Btc);
        settings.Appearance.Layout = WatchlistLayout.Cards;
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();

        h.Main.SetFullScreen(true);
        h.Main.SetFullScreen(false);

        h.Main.IsCardLayout.Should().BeTrue();
    }

    /// <summary>
    /// Verifies the card size command saves the size, hands it to every row and updates the menu check marks.
    /// </summary>
    [Fact]
    public async Task SetCardSize_SavesAndAppliesToRows()
    {
        await using var h = new Harness(Harness.WatchlistOf(Aapl, Btc));
        await h.Main.InitializeAsync();
        h.Main.CardSize.Should().Be(CardSize.Medium);
        h.Main.IsCardSizeMedium.Should().BeTrue();

        await h.Main.SetCardSizeCommand.ExecuteAsync(CardSize.Large);

        h.Main.CardSize.Should().Be(CardSize.Large);
        h.Main.IsCardSizeLarge.Should().BeTrue();
        h.Main.IsCardSizeMedium.Should().BeFalse();
        h.Main.Rows.Should().OnlyContain(r => r.CardSize == CardSize.Large && r.IsLargeCard && !r.IsSmallCard);
        h.Store.Stored!.Appearance.CardSize.Should().Be(CardSize.Large);
    }

    /// <summary>
    /// Verifies the size chosen in the settings reaches the rows, and a saved size is used when the app starts.
    /// </summary>
    [Fact]
    public async Task CardSize_FromSettingsPageAndFromSavedSettings()
    {
        var settings = Harness.WatchlistOf(Aapl);
        settings.Appearance.CardSize = CardSize.Small;
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();
        h.Main.Rows.Single().CardSize.Should().Be(CardSize.Small);

        h.Main.Settings.SelectedCardSize = CardSize.Large;

        await Harness.EventuallyAsync(() => h.Store.Stored!.Appearance.CardSize == CardSize.Large);
        await Harness.EventuallyAsync(() => h.Main.Rows.Single().CardSize == CardSize.Large);
    }

    /// <summary>
    /// Verifies a live quote reaches the row.
    /// </summary>
    [Fact]
    public async Task Quote_FromProvider_UpdatesRow()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));
        await h.Main.InitializeAsync();

        h.CryptoFactory.Fake.Emit(new Quote(Btc.Id, 105m, 100m, h.Time.GetUtcNow()));

        await Harness.EventuallyAsync(() => h.Main.Rows.Single().PriceText == "105.00");
        h.Main.Rows.Single().ChangeText.Should().Be("+5.00%");
    }

    /// <summary>
    /// Verifies removing and moving rows persists the new watchlist.
    /// </summary>
    [Fact]
    public async Task RemoveAndMove_PersistWatchlist()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc, Aapl));
        await h.Main.InitializeAsync();

        await h.Main.MoveToAsync(h.Main.Rows[0], 1);
        await Harness.EventuallyAsync(() => h.Main.Rows[0].Symbol == "AAPL");
        h.Store.Stored!.Watchlist.Select(e => e.Symbol).Should().Equal("AAPL", "BTC");

        await h.Main.RemoveCommand.ExecuteAsync(h.Main.Rows[0]);
        await Harness.EventuallyAsync(() => h.Main.Rows.Count == 1);
        h.Store.Stored.Watchlist.Select(e => e.Symbol).Should().Equal("BTC");
    }

    /// <summary>
    /// Verifies moving a row to an absolute index reorders the rows and persists the watchlist.
    /// </summary>
    [Fact]
    public async Task MoveToAsync_FirstRowToLastIndex_ReordersAndPersists()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc, Aapl, Eth));
        await h.Main.InitializeAsync();

        await h.Main.MoveToAsync(h.Main.Rows[0], 2);

        h.Main.Rows.Select(r => r.Symbol).Should().Equal("AAPL", "ETH", "BTC");
        await Harness.EventuallyAsync(() => h.Store.Stored!.Watchlist[0].Symbol == "AAPL");
        h.Store.Stored!.Watchlist.Select(e => e.Symbol).Should().Equal("AAPL", "ETH", "BTC");
    }

    /// <summary>
    /// Verifies a target index past the end is clamped to the last position.
    /// </summary>
    [Fact]
    public async Task MoveToAsync_IndexOutOfRange_ClampsToEnds()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc, Aapl, Eth));
        await h.Main.InitializeAsync();

        await h.Main.MoveToAsync(h.Main.Rows[0], 99);
        await h.Main.MoveToAsync(h.Main.Rows[1], -5);

        await Harness.EventuallyAsync(() => h.Store.Stored!.Watchlist[0].Symbol == "ETH");
        h.Store.Stored!.Watchlist.Select(e => e.Symbol).Should().Equal("ETH", "AAPL", "BTC");
        h.Main.Rows.Select(r => r.Symbol).Should().Equal("ETH", "AAPL", "BTC");
    }

    /// <summary>
    /// Verifies moving a row to its current index leaves the order untouched.
    /// </summary>
    [Fact]
    public async Task MoveToAsync_SameIndex_KeepsOrder()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc, Aapl));
        await h.Main.InitializeAsync();

        await h.Main.MoveToAsync(h.Main.Rows[1], 1);

        h.Main.Rows.Select(r => r.Symbol).Should().Equal("BTC", "AAPL");
        h.Store.Stored.Should().NotBeNull();
        h.Store.Stored!.Watchlist.Select(e => e.Symbol).Should().Equal("BTC", "AAPL");
    }

    /// <summary>
    /// Verifies moving a row that has already been removed, leaving an empty watchlist, is ignored rather than throwing.
    /// </summary>
    [Fact]
    public async Task MoveToAsync_RowRemovedFromEmptyWatchlist_IsIgnored()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));
        await h.Main.InitializeAsync();
        var staleRow = h.Main.Rows.Single();
        await h.Main.RemoveCommand.ExecuteAsync(staleRow);
        await Harness.EventuallyAsync(() => h.Main.Rows.Count == 0);

        var act = () => h.Main.MoveToAsync(staleRow, 0);

        await act.Should().NotThrowAsync();
        h.Store.Stored!.Watchlist.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies the compact setting hides every sparkline.
    /// </summary>
    [Fact]
    public async Task Compact_Toggled_HidesSparklines()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));
        await h.Main.InitializeAsync();

        h.Main.Settings.Compact = true;

        h.Main.Rows.Single().ShowSparkline.Should().BeFalse();
        await Harness.EventuallyAsync(() => h.Store.Stored!.Appearance.Compact);
    }

    /// <summary>
    /// Verifies rows do not flash on price changes by default.
    /// </summary>
    [Fact]
    public async Task Quote_PriceChangesWithDefaultSettings_DoesNotFlash()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));
        await h.Main.InitializeAsync();

        await EmitPricesAsync(h, 100m, 101m);

        h.Main.Settings.RollPriceOnUpdate.Should().BeFalse();
        h.Main.Rows.Single().RollVersion.Should().Be(0);
    }

    /// <summary>
    /// Verifies the persisted flash setting is applied to the rows built at startup.
    /// </summary>
    [Fact]
    public async Task Quote_PriceChangesWithPersistedFlashOn_FlashesRow()
    {
        var settings = Harness.WatchlistOf(Btc);
        settings.Appearance.RollPriceOnUpdate = true;
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();

        await EmitPricesAsync(h, 100m, 101m);

        h.Main.Rows.Single().RollVersion.Should().Be(1);
        h.Main.Rows.Single().RollUp.Should().BeTrue();
    }

    /// <summary>
    /// Verifies edge hiding is offered at first, and can be withdrawn while the application runs.
    /// </summary>
    [Fact]
    public async Task AutoHideAvailable_StartsAvailableAndCanBeWithdrawn()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));
        await h.Main.InitializeAsync();

        h.Main.Settings.AutoHideAvailable.Should().BeTrue();

        h.Main.Settings.AutoHideAvailable = false;

        h.Main.Settings.AutoHideAvailable.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the edge auto-hide setting is persisted.
    /// </summary>
    [Fact]
    public async Task AutoHideAtEdge_Toggled_Persists()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));
        await h.Main.InitializeAsync();

        h.Main.Settings.AutoHideAtEdge = false;

        await Harness.EventuallyAsync(() => !h.Store.Stored!.Appearance.AutoHideAtEdge);
    }

    /// <summary>
    /// Verifies the flash setting reaches existing rows and is persisted.
    /// </summary>
    [Fact]
    public async Task FlashOnUpdate_Toggled_UpdatesRowsAndPersists()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));
        await h.Main.InitializeAsync();

        h.Main.Settings.RollPriceOnUpdate = true;

        h.Main.Rows.Single().RollEnabled.Should().BeTrue();
        await Harness.EventuallyAsync(() => h.Store.Stored!.Appearance.RollPriceOnUpdate);
    }

    /// <summary>
    /// Emits the given prices for the Bitcoin asset in order, waiting for each one to reach its row.
    /// </summary>
    /// <param name="h">
    /// The harness to emit through.
    /// </param>
    /// <param name="prices">
    /// The prices to emit.
    /// </param>
    /// <returns>
    /// A task that completes once the last price is shown.
    /// </returns>
    private static async Task EmitPricesAsync(Harness h, params decimal[] prices)
    {
        foreach (var price in prices)
        {
            h.CryptoFactory.Fake.Emit(new Quote(Btc.Id, price, 100m, h.Time.GetUtcNow()));
            await Harness.EventuallyAsync(() => h.Main.Rows.Single().PriceText == $"{price:0.00}");
        }
    }

    /// <summary>
    /// Verifies opening the add instrument page prepares it and raises a request for its window.
    /// </summary>
    [Fact]
    public async Task ShowAdd_PreparesPageAndRaisesRequest()
    {
        await using var h = new Harness();
        await h.Main.InitializeAsync();
        h.Main.IsEmpty.Should().BeTrue();
        var requests = 0;
        h.Main.AddAssetRequested += (_, _) => requests++;

        h.Main.ShowAddCommand.Execute(null);

        requests.Should().Be(1);
        h.AddAsset.Title.Should().Be("Add instrument");
        h.AddAsset.CanChangeClass.Should().BeTrue();
    }

    /// <summary>
    /// Verifies opening settings raises a request for the settings window each time.
    /// </summary>
    [Fact]
    public async Task ShowSettings_RaisesRequest()
    {
        await using var h = new Harness();
        await h.Main.InitializeAsync();
        var requests = 0;
        h.Main.SettingsRequested += (_, _) => requests++;

        h.Main.ShowSettingsCommand.Execute(null);
        h.Main.ShowSettingsCommand.Execute(null);

        requests.Should().Be(2);
    }

    /// <summary>
    /// Verifies no window is requested and no page state is reset while a tool window is already open.
    /// </summary>
    [Fact]
    public async Task WhileToolWindowIsOpen_RequestsAreIgnoredAndStateIsKept()
    {
        await using var h = new Harness(Harness.WatchlistOf(Aapl));
        await h.Main.InitializeAsync();
        h.Main.ShowAddCommand.Execute(null);
        h.AddAsset.Query = "kept";
        h.Main.BeginToolWindow(ToolWindowKind.AddAsset);
        h.Main.IsToolWindowOpen.Should().BeTrue();
        h.Main.IsSettingsOpen.Should().BeFalse();
        var requests = 0;
        h.Main.AddAssetRequested += (_, _) => requests++;
        h.Main.SettingsRequested += (_, _) => requests++;

        h.Main.ShowAddCommand.Execute(null);
        h.Main.ShowSettingsCommand.Execute(null);
        h.Main.ChangeSourceCommand.Execute(h.Main.Rows[0]);

        requests.Should().Be(0);
        h.AddAsset.Query.Should().Be("kept");
        h.AddAsset.Title.Should().Be("Add instrument");
    }

    /// <summary>
    /// Verifies requests are served again once the tool window is closed.
    /// </summary>
    [Fact]
    public async Task AfterToolWindowCloses_RequestsAreServedAgain()
    {
        await using var h = new Harness();
        await h.Main.InitializeAsync();
        var requests = 0;
        h.Main.SettingsRequested += (_, _) => requests++;
        h.Main.BeginToolWindow(ToolWindowKind.Settings);
        h.Main.IsSettingsOpen.Should().BeTrue();
        h.Main.ShowSettingsCommand.Execute(null);

        h.Main.EndToolWindow();
        h.Main.IsToolWindowOpen.Should().BeFalse();
        h.Main.ShowSettingsCommand.Execute(null);

        requests.Should().Be(1);
    }

    /// <summary>
    /// Verifies data sources start collapsed, also after the page is reloaded, and expand independently.
    /// </summary>
    [Fact]
    public async Task ProviderEntries_StartCollapsedAndExpandIndependently()
    {
        await using var h = new Harness();
        await h.Main.InitializeAsync();
        h.SettingsPage.Reload();
        var providers = h.SettingsPage.Providers;
        providers.Should().OnlyContain(p => !p.IsExpanded);

        providers[0].IsExpanded = true;

        providers.Skip(1).Should().OnlyContain(p => !p.IsExpanded);
        providers[0].IsExpanded = false;
        providers[0].IsExpanded.Should().BeFalse();
    }

    /// <summary>
    /// Verifies switching a data source on opens its options so required settings are not hidden.
    /// </summary>
    [Fact]
    public async Task EnablingProvider_ExpandsItsOptions()
    {
        await using var h = new Harness();
        await h.Main.InitializeAsync();
        var provider = h.SettingsPage.Providers.First();
        provider.Enabled = false;
        provider.IsExpanded = false;

        provider.Enabled = true;

        provider.IsExpanded.Should().BeTrue();
    }

    /// <summary>
    /// Verifies an invalid option opens the section and flags it, and a valid value clears the flag.
    /// </summary>
    [Fact]
    public async Task InvalidOption_ExpandsProviderAndFlagsError()
    {
        await using var h = new Harness();
        await h.Main.InitializeAsync();
        var provider = h.SettingsPage.Providers.First();
        var interval = provider.Options.Single(o => o.Definition.Key == "Interval");
        provider.IsExpanded = false;
        provider.HasOptionError.Should().BeFalse();

        interval.Text = "not a number";

        provider.HasOptionError.Should().BeTrue();
        provider.IsExpanded.Should().BeTrue();
        interval.Text = "10";
        provider.HasOptionError.Should().BeFalse();
    }

    /// <summary>
    /// Verifies restoring defaults resets every option of a source, saves the defaults and keeps the enabled state.
    /// </summary>
    [Fact]
    public async Task RestoreDefaults_ResetsOptionsPersistsAndKeepsEnabledState()
    {
        await using var h = new Harness();
        await h.Main.InitializeAsync();
        var provider = h.SettingsPage.Providers.First();
        var id = provider.Descriptor.Id;
        var interval = provider.Options.Single(o => o.Definition.Key == "Interval");
        var url = provider.Options.Single(o => o.Definition.Key == "Url");
        interval.Text = "10";
        url.Text = "https://b";
        await Harness.EventuallyAsync(() => h.Store.Stored!.Providers[id].Options.GetValueOrDefault("Interval") == "00:00:10");
        var enabled = provider.Enabled;

        provider.RestoreDefaultsCommand.Execute(null);

        interval.Text.Should().Be("5");
        url.Text.Should().Be("https://a");
        provider.Enabled.Should().Be(enabled);

        // Defaults are not written out, so they keep following the default of the installed version.
        await Harness.EventuallyAsync(() => h.Store.Stored!.Providers[id].Options.Count == 0);
    }

    /// <summary>
    /// Verifies only options that differ from the default are persisted.
    /// </summary>
    [Fact]
    public async Task CollectOptions_LeavesOutValuesAtTheirDefault()
    {
        await using var h = new Harness();
        await h.Main.InitializeAsync();
        var provider = h.SettingsPage.Providers.First();
        provider.CollectOptions().Should().BeEmpty();

        provider.Options.Single(o => o.Definition.Key == "Interval").Text = "10";

        provider.CollectOptions().Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>("Interval", "00:00:10"));
    }

    /// <summary>
    /// Verifies settings written by earlier versions, which stored every option explicitly, load as defaults
    /// and are written back without the values that equal the default.
    /// </summary>
    [Fact]
    public async Task ExplicitDefaultsFromEarlierVersions_AreDroppedOnTheNextSave()
    {
        var settings = new AppSettings();
        settings.Providers[Harness.Crypto] = new ProviderSettings { Options = { ["Interval"] = "00:00:05", ["Url"] = "https://a" } };
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();
        h.SettingsPage.Reload();
        var provider = h.SettingsPage.Providers.Single(p => p.Descriptor.Id == Harness.Crypto);
        provider.Options.Single(o => o.Definition.Key == "Interval").Text.Should().Be("5");
        provider.CollectOptions().Should().BeEmpty();

        provider.Options.Single(o => o.Definition.Key == "Url").Text = "https://b";

        await Harness.EventuallyAsync(() => h.Store.Stored!.Providers[Harness.Crypto].Options.Count == 1);
        h.Store.Stored!.Providers[Harness.Crypto].Options.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>("Url", "https://b"));
    }

    /// <summary>
    /// Verifies an invalid input in one option does not drop the saved value of that option when another option is saved.
    /// </summary>
    [Fact]
    public async Task InvalidInput_DoesNotRevertTheSavedValueWhenAnotherOptionIsSaved()
    {
        await using var h = new Harness();
        await h.Main.InitializeAsync();
        var provider = h.SettingsPage.Providers.First();
        var id = provider.Descriptor.Id;
        var interval = provider.Options.Single(o => o.Definition.Key == "Interval");
        interval.Text = "10";
        await Harness.EventuallyAsync(() => h.Store.Stored!.Providers[id].Options.GetValueOrDefault("Interval") == "00:00:10");

        interval.Text = "not a number";
        provider.Options.Single(o => o.Definition.Key == "Url").Text = "https://b";

        await Harness.EventuallyAsync(() => h.Store.Stored!.Providers[id].Options.GetValueOrDefault("Url") == "https://b");
        h.Store.Stored!.Providers[id].Options["Interval"].Should().Be("00:00:10");
        interval.Error.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies restoring only saves when it changes something, so clicking it on an unchanged source does no work.
    /// </summary>
    [Fact]
    public void RestoreDefaults_SavesOnlyWhenSomethingChanged()
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

        provider.RestoreDefaultsCommand.Execute(null);
        saves.Should().Be(0, "everything is already at the default");

        provider.Options.Single().Text = "https://b";
        saves.Should().Be(1, "committing the edit saves");
        provider.RestoreDefaultsCommand.Execute(null);

        saves.Should().Be(2);
        provider.Options.Single().Text.Should().Be("https://a");
    }

    /// <summary>
    /// Verifies restoring defaults leaves secret options such as keys untouched.
    /// </summary>
    [Fact]
    public void RestoreDefaults_KeepsSecretOptions()
    {
        var descriptor = new ProviderDescriptor(
            "secret",
            "Secret",
            "Test",
            [AssetClass.Crypto],
            ProviderUpdateMode.Streaming,
            Options:
            [
                new ProviderOptionDefinition("Url", "Url", ProviderOptionType.String, "https://a"),
                new ProviderOptionDefinition("Key", "Key", ProviderOptionType.Secret, string.Empty),
            ]);
        var provider = new ProviderSettingsViewModel(descriptor, _ => { });
        provider.Options.Single(o => o.Definition.Key == "Url").Text = "https://b";
        provider.Options.Single(o => o.Definition.Key == "Key").Text = "my-key";

        provider.RestoreDefaultsCommand.Execute(null);

        provider.Options.Single(o => o.Definition.Key == "Url").Text.Should().Be("https://a");
        provider.Options.Single(o => o.Definition.Key == "Key").Text.Should().Be("my-key");
        provider.CollectOptions().Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>("Key", "my-key"));
    }

    /// <summary>
    /// Verifies restoring defaults also clears an invalid input and its error flag.
    /// </summary>
    [Fact]
    public async Task RestoreDefaults_AfterInvalidInput_ClearsTheError()
    {
        await using var h = new Harness();
        await h.Main.InitializeAsync();
        var provider = h.SettingsPage.Providers.First();
        var interval = provider.Options.Single(o => o.Definition.Key == "Interval");
        interval.Text = "not a number";
        provider.HasOptionError.Should().BeTrue();

        provider.RestoreDefaultsCommand.Execute(null);

        interval.Error.Should().BeEmpty();
        provider.HasOptionError.Should().BeFalse();
        interval.Text.Should().Be("5");
    }

    /// <summary>
    /// Verifies restoring is always possible: with every option already at its default it simply keeps the defaults.
    /// </summary>
    [Fact]
    public async Task RestoreDefaults_WhenAlreadyAtDefaults_KeepsTheDefaults()
    {
        await using var h = new Harness();
        await h.Main.InitializeAsync();
        var provider = h.SettingsPage.Providers.First();
        var interval = provider.Options.Single(o => o.Definition.Key == "Interval");

        provider.RestoreDefaultsCommand.CanExecute(null).Should().BeTrue();
        provider.RestoreDefaultsCommand.Execute(null);

        interval.Text.Should().Be("5");
        provider.CollectOptions().Should().BeEmpty();
    }

    /// <summary>
    /// Verifies an invalid option is only flagged while its data source is enabled, because a disabled source hides its options.
    /// </summary>
    [Fact]
    public async Task InvalidOption_OfDisabledProvider_IsNotFlagged()
    {
        await using var h = new Harness();
        await h.Main.InitializeAsync();
        var provider = h.SettingsPage.Providers.First();
        provider.Options.Single(o => o.Definition.Key == "Interval").Text = "not a number";
        provider.HasOptionError.Should().BeTrue();

        provider.Enabled = false;

        provider.HasOptionError.Should().BeFalse();
        provider.Enabled = true;
        provider.HasOptionError.Should().BeTrue();
    }

    /// <summary>
    /// Verifies expanding a data source neither changes its enabled state nor publishes any settings change.
    /// </summary>
    [Fact]
    public async Task ExpandingProvider_DoesNotChangeEnabledStateOrWriteSettings()
    {
        await using var h = new Harness();
        await h.Main.InitializeAsync();
        var provider = h.SettingsPage.Providers.First();
        var enabled = provider.Enabled;
        var changes = 0;
        h.Settings.Changed += (_, _) => changes++;

        provider.IsExpanded = true;

        // Updates are serialized, so a barrier update completes after any update the expansion might have started;
        // it is the only change that may be published.
        await h.Settings.UpdateAsync(_ => { });
        changes.Should().Be(1);
        provider.Enabled.Should().Be(enabled);
    }

    /// <summary>
    /// Verifies a disabled data source marks its rows instead of dropping them.
    /// </summary>
    [Fact]
    public async Task DisablingProvider_MarksRowsAsSourceOff()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc, Aapl));
        await h.Main.InitializeAsync();

        var stocks = h.SettingsPage.Providers.Single(p => p.Descriptor.Id == Harness.Stocks);
        stocks.Enabled = false;

        await Harness.EventuallyAsync(() => h.Main.Rows.Single(r => r.Symbol == "AAPL").StatusText == "Source off");
        h.Main.Rows.Single(r => r.Symbol == "BTC").StatusText.Should().BeEmpty();
        h.Store.Stored!.Providers[Harness.Stocks].Enabled.Should().BeFalse();
    }

    /// <summary>
    /// Verifies adding an instrument from search results saves it and finishes the page.
    /// </summary>
    [Fact]
    public async Task AddAsync_FromSearchResult_SavesAndCompletes()
    {
        await using var h = new Harness();
        await h.Main.InitializeAsync();
        h.StocksFactory.Fake.SearchResults = [Aapl];
        var completed = 0;
        h.AddAsset.Completed += (_, _) => completed++;
        h.Main.ShowAddCommand.Execute(null);
        h.AddAsset.SelectedAssetClass = AssetClass.Equity;
        h.AddAsset.SelectedProvider = h.AddAsset.AvailableProviders.Single(p => p.Id == Harness.Stocks);
        h.AddAsset.Query = "app";
        h.Time.Advance(TimeSpan.FromSeconds(1));

        await Harness.EventuallyAsync(() => h.AddAsset.Results.Count == 1);
        await h.AddAsset.AddCommand.ExecuteAsync(h.AddAsset.Results[0]);

        h.Store.Stored!.Watchlist.Should().ContainSingle().Which.Symbol.Should().Be("AAPL");
        completed.Should().Be(1);
        await Harness.EventuallyAsync(() => h.Main.Rows.Count == 1);
    }

    /// <summary>
    /// Verifies adding an instrument that is already tracked is refused with a message.
    /// </summary>
    [Fact]
    public async Task AddAsync_Duplicate_ShowsMessageAndStays()
    {
        await using var h = new Harness(Harness.WatchlistOf(Aapl));
        await h.Main.InitializeAsync();
        var completed = 0;
        h.AddAsset.Completed += (_, _) => completed++;
        h.Main.ShowAddCommand.Execute(null);

        await h.AddAsset.AddCommand.ExecuteAsync(Aapl);

        h.AddAsset.Message.Should().Contain("already");
        completed.Should().Be(0);
        h.Store.Stored!.Watchlist.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies the provider list is limited to enabled sources that support the class, best first.
    /// </summary>
    [Fact]
    public async Task BeginAdd_ListsOnlySourcesSupportingTheClass()
    {
        await using var h = new Harness();
        await h.Main.InitializeAsync();

        h.AddAsset.BeginAdd();
        h.AddAsset.SelectedAssetClass = AssetClass.Crypto;

        h.AddAsset.AvailableProviders.Select(p => p.Id).Should().Equal(Harness.Crypto);
        h.AddAsset.SelectedProvider!.Id.Should().Be(Harness.Crypto);

        h.AddAsset.SelectedAssetClass = AssetClass.Equity;
        h.AddAsset.AvailableProviders.Select(p => p.Id).Should().Equal(Harness.Stocks, Harness.Plain);
    }

    /// <summary>
    /// Verifies a source that cannot search lets the user add the typed symbol.
    /// </summary>
    [Fact]
    public async Task AddTypedAsync_WithSourceWithoutSearch_AddsUppercasedSymbol()
    {
        await using var h = new Harness();
        await h.Main.InitializeAsync();
        h.AddAsset.BeginAdd();
        h.AddAsset.SelectedAssetClass = AssetClass.Equity;
        h.AddAsset.SelectedProvider = h.AddAsset.AvailableProviders.Single(p => p.Id == Harness.Plain);
        h.AddAsset.Query = "msft";

        await Harness.EventuallyAsync(() => !h.AddAsset.CanSearch);
        await h.AddAsset.AddTypedCommand.ExecuteAsync(null);

        var entry = h.Store.Stored!.Watchlist.Should().ContainSingle().Subject;
        (entry.ProviderId, entry.Symbol).Should().Be((Harness.Plain, "MSFT"));
    }

    /// <summary>
    /// Verifies moving an instrument to another data source keeps its position.
    /// </summary>
    [Fact]
    public async Task ChangeSource_ReplacesEntryInPlace()
    {
        var first = new Asset(Harness.Stocks, "A", "A", AssetClass.Equity);
        var second = new Asset(Harness.Stocks, "B", "B", AssetClass.Equity);
        var third = new Asset(Harness.Stocks, "C", "C", AssetClass.Equity);
        await using var h = new Harness(Harness.WatchlistOf(first, second, third));
        await h.Main.InitializeAsync();
        var target = new Asset(Harness.Plain, "B", "B", AssetClass.Equity);

        var requests = 0;
        h.Main.AddAssetRequested += (_, _) => requests++;

        h.Main.ChangeSourceCommand.Execute(h.Main.Rows[1]);
        requests.Should().Be(1);
        h.AddAsset.SelectedProvider.Should().NotBeNull();
        h.AddAsset.SelectedProvider!.Id.Should().Be(Harness.Plain, "the current source is avoided when an alternative exists");
        await h.AddAsset.AddCommand.ExecuteAsync(target);

        h.Store.Stored!.Watchlist.Select(e => (e.ProviderId, e.Symbol)).Should().Equal(
            (Harness.Stocks, "A"),
            (Harness.Plain, "B"),
            (Harness.Stocks, "C"));
    }

    /// <summary>
    /// Verifies moving an instrument to another data source takes its alerts along, and leaves those of other instruments.
    /// </summary>
    [Fact]
    public async Task ChangeSource_KeepsTheAlertsOfTheInstrument()
    {
        var first = new Asset(Harness.Stocks, "A", "A", AssetClass.Equity);
        var second = new Asset(Harness.Stocks, "B", "B", AssetClass.Equity);
        var settings = Harness.WatchlistOf(first, second);
        settings.Alerts.Add(new AlertRule { Id = "a", ProviderId = Harness.Stocks, Symbol = "A", Threshold = 5 });
        settings.Alerts.Add(new AlertRule { Id = "b-up", ProviderId = Harness.Stocks, Symbol = "B", Condition = AlertCondition.PriceAbove, Threshold = 10 });
        settings.Alerts.Add(new AlertRule { Id = "b-down", ProviderId = Harness.Stocks, Symbol = "B", Condition = AlertCondition.PriceBelow, Threshold = 2 });
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();

        var told = new List<AlertsMovedEventArgs>();
        h.AddAsset.AlertsMoved += (_, e) => told.Add(e);

        h.Main.ChangeSourceCommand.Execute(h.Main.Rows[1]);
        await h.AddAsset.AddCommand.ExecuteAsync(new Asset(Harness.Plain, "B", "B", AssetClass.Equity));

        h.Store.Stored!.Alerts.Select(a => (a.Id, a.ProviderId, a.Symbol)).Should().Equal(
            ("a", Harness.Stocks, "A"),
            ("b-up", Harness.Plain, "B"),
            ("b-down", Harness.Plain, "B"));
        told.Should().ContainSingle().Which.Should().Match<AlertsMovedEventArgs>(e => e.Symbol == "B" && e.Count == 2 && e.ProviderName.Length > 0);
    }

    /// <summary>
    /// Verifies moving an instrument to another data source keeps what the user holds of it.
    /// </summary>
    [Fact]
    public async Task ChangeSource_KeepsTheHoldingOfTheInstrument()
    {
        var settings = Harness.WatchlistOf(new Asset(Harness.Stocks, "A", "A", AssetClass.Equity), new Asset(Harness.Stocks, "B", "B", AssetClass.Equity));
        settings.Watchlist[1].CostPrice = 12.5m;
        settings.Watchlist[1].Quantity = 40m;
        settings.Watchlist[1].HoldingCurrency = "HKD";
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();

        h.Main.ChangeSourceCommand.Execute(h.Main.Rows[1]);
        await h.AddAsset.AddCommand.ExecuteAsync(new Asset(Harness.Plain, "B", "B", AssetClass.Equity));

        var moved = h.Store.Stored!.Watchlist.Single(e => e.ProviderId == Harness.Plain);
        moved.CostPrice.Should().Be(12.5m);
        moved.Quantity.Should().Be(40m);
        moved.HoldingCurrency.Should().Be("HKD");
        h.Store.Stored.Watchlist.Single(e => e.Symbol == "A").HasHolding.Should().BeFalse();
    }

    /// <summary>
    /// Verifies changing the data source is refused while the alerts would clash with those the instrument already has at
    /// the new source, with a message that names the condition; once the clash is deleted, the change goes through.
    /// </summary>
    [Fact]
    public async Task ChangeSource_AlertsClash_IsRefusedUntilTheyAreDeleted()
    {
        var here = new Asset(Harness.Stocks, "B", "B", AssetClass.Equity);
        var there = new Asset(Harness.Plain, "B", "B", AssetClass.Equity);
        var settings = Harness.WatchlistOf(here, there);
        settings.Alerts.Add(new AlertRule { Id = "here-up", ProviderId = Harness.Stocks, Symbol = "B", Condition = AlertCondition.PriceAbove, Threshold = 10 });
        settings.Alerts.Add(new AlertRule { Id = "there-up", ProviderId = Harness.Plain, Symbol = "B", Condition = AlertCondition.PriceAbove, Threshold = 20 });
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();
        var completed = 0;
        h.AddAsset.Completed += (_, _) => completed++;

        h.Main.ChangeSourceCommand.Execute(h.Main.Rows[0]);
        await h.AddAsset.AddCommand.ExecuteAsync(there);

        completed.Should().Be(0);
        var names = h.AddAsset.AvailableProviders.Select(ProviderText.Name).ToList();
        names.Should().Contain(n => n.Length > 0);
        h.AddAsset.Message.Should().Contain("B").And.Contain(ProviderText.Name(h.AddAsset.AvailableProviders.Single(d => d.Id == Harness.Stocks)))
            .And.Contain(ProviderText.Name(h.AddAsset.AvailableProviders.Single(d => d.Id == Harness.Plain)), "it names both sources, so it is clear that either alert can go");
        h.Store.Stored!.Watchlist.Select(e => e.ProviderId).Should().Equal(Harness.Stocks, Harness.Plain);
        h.Store.Stored.Alerts.Select(a => a.Id).Should().Equal("here-up", "there-up");

        await h.Settings.UpdateAsync(s => s.Alerts.RemoveAll(a => a.Id == "there-up"));
        h.Main.ChangeSourceCommand.Execute(h.Main.Rows[0]);
        await h.AddAsset.AddCommand.ExecuteAsync(there);

        completed.Should().Be(1);
        h.Store.Stored!.Alerts.Should().ContainSingle().Which.ProviderId.Should().Be(Harness.Plain);
    }

    /// <summary>
    /// Verifies the user is not told about moved alerts when an instrument without alerts changes its data source.
    /// </summary>
    [Fact]
    public async Task ChangeSource_WithoutAlerts_SaysNothing()
    {
        var first = new Asset(Harness.Stocks, "A", "A", AssetClass.Equity);
        await using var h = new Harness(Harness.WatchlistOf(first));
        await h.Main.InitializeAsync();
        var told = 0;
        h.AddAsset.AlertsMoved += (_, _) => told++;

        h.Main.ChangeSourceCommand.Execute(h.Main.Rows[0]);
        await h.AddAsset.AddCommand.ExecuteAsync(new Asset(Harness.Plain, "A", "A", AssetClass.Equity));

        told.Should().Be(0);
    }

    /// <summary>
    /// Verifies the settings page filling its controls from the saved settings is not a choice of the list layout: a window
    /// that is full screen stays so when Settings is opened.
    /// </summary>
    [Fact]
    public async Task OpeningSettingsWhileFullScreen_DoesNotLeaveFullScreen()
    {
        var settings = Harness.WatchlistOf(Aapl, Btc);
        settings.Appearance.Layout = WatchlistLayout.Cards;
        await using var h = new Harness(settings);
        await h.Main.InitializeAsync();

        // The layout is changed without the settings page: its control still shows the cards.
        await h.Settings.UpdateAsync(s => s.Appearance.Layout = WatchlistLayout.List);
        h.Main.SetFullScreen(true);
        var requests = 0;
        h.Main.FullScreenExitRequested += (_, _) => requests++;

        h.Main.Settings.Reload();

        h.Main.Settings.SelectedLayout.Should().Be(WatchlistLayout.List);
        requests.Should().Be(0);

        // A choice of the user in the settings does leave full screen.
        h.Main.Settings.SelectedLayout = WatchlistLayout.Cards;
        h.Main.Settings.SelectedLayout = WatchlistLayout.List;
        requests.Should().Be(1);
    }

    /// <summary>
    /// Verifies the routing choice is persisted as the preferred source followed by the others.
    /// </summary>
    [Fact]
    public async Task Routing_Selection_PersistsOrder()
    {
        await using var h = new Harness();
        await h.Main.InitializeAsync();
        var row = h.SettingsPage.Routing.Single(r => r.AssetClass == AssetClass.Equity);

        row.Selected = row.Providers.Single(p => p.Id == Harness.Plain);

        await Harness.EventuallyAsync(() => h.Store.Stored!.Routing.ContainsKey(AssetClass.Equity));
        h.Store.Stored!.Routing[AssetClass.Equity].Should().Equal(Harness.Plain, Harness.Stocks);
        h.Router.GetPreferred(AssetClass.Equity, h.Settings.Current)!.Id.Should().Be(Harness.Plain);
    }

    /// <summary>
    /// Verifies a valid option edit is persisted and an invalid one is not.
    /// </summary>
    [Fact]
    public async Task ProviderOption_EditPersistsOnlyValidValues()
    {
        await using var h = new Harness();
        await h.Main.InitializeAsync();
        var provider = h.SettingsPage.Providers.Single(p => p.Descriptor.Id == Harness.Crypto);
        var interval = provider.Options.Single(o => o.Definition.Key == "Interval");

        interval.Text = "30";
        await Harness.EventuallyAsync(() => h.Store.Stored!.Providers.ContainsKey(Harness.Crypto));
        h.Store.Stored!.Providers[Harness.Crypto].Options["Interval"].Should().Be("00:00:30");

        interval.Text = "1";
        interval.Error.Should().NotBeEmpty();
        h.Store.Stored.Providers[Harness.Crypto].Options["Interval"].Should().Be("00:00:30");
    }

    /// <summary>
    /// Verifies the opacity is saved once after the slider settled rather than on every step.
    /// </summary>
    [Fact]
    public async Task Opacity_Dragged_IsSavedOnceAfterQuietPeriod()
    {
        await using var h = new Harness();
        await h.Main.InitializeAsync();

        h.SettingsPage.Opacity = 0.8;
        h.SettingsPage.Opacity = 0.7;
        h.SettingsPage.Opacity = 0.6;
        h.Store.Stored!.Appearance.Opacity.Should().NotBe(0.6);

        h.Time.Advance(TimeSpan.FromSeconds(1));

        await Harness.EventuallyAsync(() => h.Store.Stored!.Appearance.Opacity == 0.6);
    }

    /// <summary>
    /// Verifies a failing data source turns its chip bad and its tooltip carries the reason.
    /// </summary>
    [Fact]
    public async Task Chips_ProviderFails_MarkChipBadWithReason()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));
        await h.Main.InitializeAsync();

        h.CryptoFactory.Fake.ChangeStatus(ProviderConnectionState.Failed, "boom");

        var chip = h.Main.Chips.Single();
        await Harness.EventuallyAsync(() => chip.IsBad);
        chip.IsGood.Should().BeFalse();
        chip.Tooltip.Should().Contain("boom");
    }

    /// <summary>
    /// Verifies a connected data source turns its chip good and clears the problem flag.
    /// </summary>
    [Fact]
    public async Task Chips_ProviderConnects_MarkChipGood()
    {
        await using var h = new Harness(Harness.WatchlistOf(Btc));
        await h.Main.InitializeAsync();
        h.CryptoFactory.Fake.ChangeStatus(ProviderConnectionState.Failed, "boom");
        var chip = h.Main.Chips.Single();
        await Harness.EventuallyAsync(() => chip.IsBad);

        h.CryptoFactory.Fake.ChangeStatus(ProviderConnectionState.Connected);

        await Harness.EventuallyAsync(() => chip.IsGood);
        chip.IsBad.Should().BeFalse();
    }
}

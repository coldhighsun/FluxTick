using FluxTick.Abstractions;
using FluxTick.Core.Application;

namespace FluxTick.Core.Tests;

/// <summary>
/// Tests for <see cref="WatchlistTransfer"/>: writing the watchlist to a file, reading it back and merging it.
/// </summary>
public class WatchlistTransferTests
{
    /// <summary>
    /// The data sources the merge knows.
    /// </summary>
    private static readonly string[] Providers = ["binance", "yahoo"];

    /// <summary>
    /// Builds settings with two groups, three instruments (two of them with a holding) and two alerts.
    /// </summary>
    /// <returns>
    /// The settings.
    /// </returns>
    private static AppSettings Sample()
    {
        var settings = new AppSettings();
        var coins = new WatchGroup { Id = "g1", Name = "Coins" };
        var stocks = new WatchGroup { Id = "g2", Name = "Stocks, US" };
        settings.Groups.AddRange([coins, stocks]);
        settings.Watchlist.AddRange(
        [
            new WatchlistEntry { ProviderId = "binance", Symbol = "BTCUSDT", DisplayName = "Bitcoin", AssetClass = AssetClass.Crypto, GroupId = "g1", CostPrice = 25000.5m, Quantity = 0.25m, HoldingCurrency = "USD" },
            new WatchlistEntry { ProviderId = "yahoo", Symbol = "AAPL", DisplayName = "Apple \"AAPL\"", AssetClass = AssetClass.Equity, GroupId = "g2", CostPrice = 150m, Quantity = 10m },
            new WatchlistEntry { ProviderId = "yahoo", Symbol = "^GSPC", DisplayName = "S&P 500", AssetClass = AssetClass.Index, GroupId = "g2" },
        ]);
        settings.Alerts.AddRange(
        [
            new AlertRule { Id = "a1", ProviderId = "binance", Symbol = "BTCUSDT", Condition = AlertCondition.PriceAbove, Threshold = 30000, Repeat = true },
            new AlertRule { Id = "a2", ProviderId = "yahoo", Symbol = "AAPL", Condition = AlertCondition.ChangePercentBelow, Threshold = 5, Enabled = false },
        ]);
        settings.Providers["yahoo"] = new ProviderSettings { Options = { ["apiKey"] = "SECRET-KEY" } };
        return settings;
    }

    /// <summary>
    /// Parses a text that is expected to be a valid file.
    /// </summary>
    /// <param name="result">
    /// The outcome of reading it.
    /// </param>
    /// <returns>
    /// What was read.
    /// </returns>
    private static WatchlistImport Imported(ImportParseResult result)
    {
        result.Error.Should().BeNull();
        return result.Import!;
    }

    /// <summary>
    /// Verifies the JSON file holds no data source options, so that API keys do not leave the computer, and none of the
    /// appearance or window settings.
    /// </summary>
    [Fact]
    public void ExportJson_LeavesOutProviderOptionsAndOtherSettings()
    {
        var settings = Sample();
        settings.Appearance.Opacity = 0.42;
        settings.Window.X = 1234;

        var json = WatchlistTransfer.ExportJson(settings);

        json.Should().NotContain("SECRET-KEY").And.NotContain("apiKey").And.NotContain("providers")
            .And.NotContain("appearance").And.NotContain("window");
        json.Should().Contain(WatchlistTransfer.FormatId).And.Contain("BTCUSDT");
    }

    /// <summary>
    /// Verifies what is written as JSON is read back with its groups, holdings and alerts.
    /// </summary>
    [Fact]
    public void ParseJson_AfterExportJson_RestoresEverythingThatIsExported()
    {
        var import = Imported(WatchlistTransfer.ParseJson(WatchlistTransfer.ExportJson(Sample())));

        import.Entries.Select(e => (e.Entry.ProviderId, e.Entry.Symbol, e.GroupName)).Should().Equal(
            ("binance", "BTCUSDT", "Coins"), ("yahoo", "AAPL", "Stocks, US"), ("yahoo", "^GSPC", "Stocks, US"));
        var bitcoin = import.Entries[0].Entry;
        bitcoin.CostPrice.Should().Be(25000.5m);
        bitcoin.Quantity.Should().Be(0.25m);
        bitcoin.HoldingCurrency.Should().Be("USD");
        import.Entries[2].Entry.HasHolding.Should().BeFalse();
        import.Alerts.Select(a => (a.Symbol, a.Condition, a.Threshold, a.Enabled, a.Repeat)).Should().Equal(
            ("BTCUSDT", AlertCondition.PriceAbove, 30000m, true, true), ("AAPL", AlertCondition.ChangePercentBelow, 5m, false, false));
        import.Invalid.Should().Be(0);
    }

    /// <summary>
    /// Verifies text that is not a file of this app is refused, each with its own reason.
    /// </summary>
    /// <param name="text">
    /// The text.
    /// </param>
    /// <param name="expected">
    /// Why it is refused.
    /// </param>
    [Theory]
    [InlineData("not json at all", TransferError.Unreadable)]
    [InlineData("", TransferError.Unreadable)]
    [InlineData("null", TransferError.Unreadable)]
    [InlineData("{}", TransferError.WrongFormat)]
    [InlineData("""{ "format": "Something.Else", "version": 1 }""", TransferError.WrongFormat)]
    [InlineData("""{ "format": "FluxTick.Watchlist" }""", TransferError.UnsupportedVersion)]
    [InlineData("""{ "format": "FluxTick.Watchlist", "version": 0 }""", TransferError.UnsupportedVersion)]
    [InlineData("""{ "format": "FluxTick.Watchlist", "version": 99 }""", TransferError.UnsupportedVersion)]
    public void ParseJson_NotAUsableFile_SaysWhy(string text, TransferError expected)
    {
        var result = WatchlistTransfer.ParseJson(text);

        result.Import.Should().BeNull();
        result.Error.Should().Be(expected);
    }

    /// <summary>
    /// Verifies items that cannot be used are left out and counted, and the rest is still read.
    /// </summary>
    [Fact]
    public void ParseJson_BrokenItems_AreCountedAndTheRestIsRead()
    {
        const string text = """
            {
              "format": "FluxTick.Watchlist", "version": 1,
              "groups": [ null, { "id": "g", "name": "  " }, { "id": "h", "name": " Mine " } ],
              "watchlist": [
                null,
                { "providerId": "binance", "symbol": "", "assetClass": "Crypto" },
                { "providerId": "binance", "symbol": "ETHUSDT", "assetClass": "Crypto", "groupId": "h" },
                { "providerId": "binance", "symbol": "XRPUSDT", "assetClass": "Crypto", "groupId": "missing" }
              ],
              "alerts": [ null, { "providerId": "binance", "symbol": "ETHUSDT", "threshold": -1 }, { "providerId": "binance", "symbol": "ETHUSDT", "threshold": 5 } ]
            }
            """;

        var import = Imported(WatchlistTransfer.ParseJson(text));

        import.Entries.Select(e => (e.Entry.Symbol, e.GroupName)).Should().Equal(("ETHUSDT", "Mine"), ("XRPUSDT", null));
        import.Alerts.Should().ContainSingle();
        import.Invalid.Should().Be(4);
    }

    /// <summary>
    /// Verifies the table has the columns in order, quotes the fields that need it, and writes numbers the same way in
    /// every language.
    /// </summary>
    [Fact]
    public void ExportCsv_QuotesFieldsAndWritesInvariantNumbers()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
        try
        {
            var csv = WatchlistTransfer.ExportCsv(Sample());

            csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Should().Equal(
                "provider,symbol,name,class,group,cost,quantity,currency",
                "binance,BTCUSDT,Bitcoin,Crypto,Coins,25000.5,0.25,USD",
                "yahoo,AAPL,\"Apple \"\"AAPL\"\"\",Equity,\"Stocks, US\",150,10,",
                "yahoo,^GSPC,S&P 500,Index,\"Stocks, US\",,,");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>
    /// Verifies what is written as a table is read back, also when a field holds quotes, commas or a line break.
    /// </summary>
    [Fact]
    public void ParseCsv_AfterExportCsv_RestoresTheInstrumentsAndHoldings()
    {
        var settings = Sample();
        settings.Watchlist[2] = new WatchlistEntry { ProviderId = "yahoo", Symbol = "^GSPC", DisplayName = "S&P\n500", AssetClass = AssetClass.Index, GroupId = "g2" };

        var import = Imported(WatchlistTransfer.ParseCsv(WatchlistTransfer.ExportCsv(settings)));

        import.Entries.Select(e => (e.Entry.Symbol, e.Entry.DisplayName, e.Entry.AssetClass, e.GroupName, e.Entry.CostPrice, e.Entry.Quantity, e.Entry.HoldingCurrency)).Should().Equal(
            ("BTCUSDT", "Bitcoin", AssetClass.Crypto, "Coins", 25000.5m, 0.25m, "USD"),
            ("AAPL", "Apple \"AAPL\"", AssetClass.Equity, "Stocks, US", 150m, 10m, null),
            ("^GSPC", "S&P\n500", AssetClass.Index, "Stocks, US", null, null, null));
        import.Alerts.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies a table written by a spreadsheet is read: a byte order mark, semicolons, other column order and case,
    /// missing optional columns, blank lines and a missing name.
    /// </summary>
    [Fact]
    public void ParseCsv_SpreadsheetStyle_IsRead()
    {
        var text = $"{(char)0xFEFF}Class;Symbol;Provider;Cost;Quantity\r\nCrypto;BTCUSDT;binance;100.5;2\r\n\r\n;;;;\r\nequity;AAPL;yahoo;;\r\n";

        var import = Imported(WatchlistTransfer.ParseCsv(text));

        import.Entries.Select(e => (e.Entry.Symbol, e.Entry.DisplayName, e.Entry.AssetClass, e.GroupName, e.Entry.CostPrice, e.Entry.Quantity)).Should().Equal(
            ("BTCUSDT", "BTCUSDT", AssetClass.Crypto, null, 100.5m, 2m),
            ("AAPL", "AAPL", AssetClass.Equity, null, null, null));
        import.Invalid.Should().Be(0);
    }

    /// <summary>
    /// Verifies rows with a missing symbol, a class that does not exist or a number that is not one are left out and
    /// counted, and a table without a needed column is refused.
    /// </summary>
    [Fact]
    public void ParseCsv_BadRowsAndColumns_AreHandled()
    {
        var import = Imported(WatchlistTransfer.ParseCsv(
            "provider,symbol,class,cost\r\nbinance,,Crypto,\r\nbinance,A,Stuff,\r\nbinance,B,Crypto,abc\r\nbinance,C,Crypto,1,extra\r\n"));

        import.Entries.Should().ContainSingle().Which.Entry.Symbol.Should().Be("C");
        import.Invalid.Should().Be(3);
        WatchlistTransfer.ParseCsv("provider,symbol\r\nbinance,BTC\r\n").Error.Should().Be(TransferError.MissingColumns);
        WatchlistTransfer.ParseCsv("   \r\n").Error.Should().Be(TransferError.Unreadable);
        WatchlistTransfer.ParseCsv(",,\r\n,,\r\n").Error.Should().Be(TransferError.Unreadable);
    }

    /// <summary>
    /// Verifies a file with only a header is a file with nothing in it, not an error.
    /// </summary>
    [Fact]
    public void ParseCsv_HeaderOnly_IsEmpty() =>
        Imported(WatchlistTransfer.ParseCsv("provider,symbol,class\r\n")).Entries.Should().BeEmpty();

    /// <summary>
    /// Verifies a merge into empty settings adds the instruments in the groups of the file, with their holdings and
    /// alerts, and that the groups that are created are the ones the file names.
    /// </summary>
    [Fact]
    public void Merge_IntoEmptySettings_AddsEverything()
    {
        var import = Imported(WatchlistTransfer.ParseJson(WatchlistTransfer.ExportJson(Sample())));
        var target = new AppSettings();

        var summary = WatchlistTransfer.Merge(target, import, Providers);

        summary.Should().Be(new ImportSummary(3, 0, 0, 0, 0, 2, 2, 0));
        target.Groups.Select(g => g.Name).Should().Equal("Coins", "Stocks, US");
        target.Watchlist.Select(e => e.Symbol).Should().Equal("BTCUSDT", "AAPL", "^GSPC");
        target.Watchlist.Select(e => target.Groups.Single(g => g.Id == e.GroupId).Name).Should().Equal("Coins", "Stocks, US", "Stocks, US");
        target.Watchlist[0].CostPrice.Should().Be(25000.5m);
        target.Alerts.Should().HaveCount(2);
        target.Alerts.Select(a => a.Id).Should().NotContain(["a1", "a2"], "the rules get ids of their own");
        target.Alerts.Single(a => a.Symbol == "AAPL").Enabled.Should().BeFalse();
    }

    /// <summary>
    /// Verifies merging the same file twice changes nothing the second time.
    /// </summary>
    [Fact]
    public void Merge_TheSameFileTwice_ChangesNothingTheSecondTime()
    {
        var import = Imported(WatchlistTransfer.ParseJson(WatchlistTransfer.ExportJson(Sample())));
        var target = new AppSettings();
        WatchlistTransfer.Merge(target, import, Providers);

        var summary = WatchlistTransfer.Merge(target, import, Providers);

        summary.ChangesNothing.Should().BeTrue();
        summary.Should().Be(new ImportSummary(0, 3, 0, 0, 0, 0, 0, 2));
        target.Watchlist.Should().HaveCount(3);
        target.Alerts.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies an instrument that is on the watchlist stays as it is, keeping its group and holding, and only gets a
    /// holding when it had none.
    /// </summary>
    [Fact]
    public void Merge_ExistingInstrument_KeepsLocalDataAndFillsAMissingHolding()
    {
        var target = new AppSettings();
        target.Groups.Add(new WatchGroup { Id = "mine", Name = "Mine" });
        target.Watchlist.AddRange(
        [
            new WatchlistEntry { ProviderId = "binance", Symbol = "BTCUSDT", DisplayName = "BTC", AssetClass = AssetClass.Crypto, GroupId = "mine", CostPrice = 1m, Quantity = 1m },
            new WatchlistEntry { ProviderId = "yahoo", Symbol = "AAPL", DisplayName = "Apple", AssetClass = AssetClass.Equity, GroupId = "mine" },
        ]);
        var import = Imported(WatchlistTransfer.ParseJson(WatchlistTransfer.ExportJson(Sample())));

        var summary = WatchlistTransfer.Merge(target, import, Providers);

        summary.Added.Should().Be(1);
        summary.Skipped.Should().Be(1);
        summary.HoldingsFilled.Should().Be(1);
        target.Watchlist[0].CostPrice.Should().Be(1m, "a holding that is there is not replaced");
        target.Watchlist[1].CostPrice.Should().Be(150m);
        target.Watchlist[1].GroupId.Should().Be("mine", "the group of an instrument that is there is not changed");
        target.Watchlist[2].Symbol.Should().Be("^GSPC");
        target.Groups.Select(g => g.Name).Should().Equal("Mine", "Stocks, US");
    }

    /// <summary>
    /// Verifies instruments of a data source this app does not have are left out and counted, a data source is
    /// recognized whatever its case, and an alert for a left-out instrument is not added.
    /// </summary>
    [Fact]
    public void Merge_UnknownProvider_IsLeftOutAndCaseOfKnownProviderDoesNotMatter()
    {
        var import = new WatchlistImport();
        import.Entries.Add(new ImportedEntry(new WatchlistEntry { ProviderId = "Binance", Symbol = "BTCUSDT", AssetClass = AssetClass.Crypto }, null));
        import.Entries.Add(new ImportedEntry(new WatchlistEntry { ProviderId = "nowhere", Symbol = "X", AssetClass = AssetClass.Equity }, null));
        import.Alerts.Add(new AlertRule { ProviderId = "NOWHERE", Symbol = "X", Threshold = 1 });
        import.Alerts.Add(new AlertRule { ProviderId = "BINANCE", Symbol = "BTCUSDT", Threshold = 1 });
        var target = new AppSettings();

        var summary = WatchlistTransfer.Merge(target, import, Providers);

        summary.Should().Be(new ImportSummary(1, 0, 0, 1, 0, 0, 1, 1));
        target.Watchlist.Should().ContainSingle().Which.ProviderId.Should().Be("binance");
        target.Alerts.Should().ContainSingle().Which.ProviderId.Should().Be("binance");
    }

    /// <summary>
    /// Verifies an instrument that is twice in the file is added once, and a group is not created for an instrument that
    /// was not added.
    /// </summary>
    [Fact]
    public void Merge_DuplicatesInTheFile_AreAddedOnce()
    {
        var import = new WatchlistImport();
        import.Entries.Add(new ImportedEntry(new WatchlistEntry { ProviderId = "binance", Symbol = "ETHUSDT", AssetClass = AssetClass.Crypto }, "A"));
        import.Entries.Add(new ImportedEntry(new WatchlistEntry { ProviderId = "binance", Symbol = "ETHUSDT", AssetClass = AssetClass.Crypto }, "B"));
        import.Entries.Add(new ImportedEntry(new WatchlistEntry { ProviderId = "nowhere", Symbol = "Z", AssetClass = AssetClass.Equity }, "C"));
        var target = new AppSettings();

        var summary = WatchlistTransfer.Merge(target, import, Providers);

        summary.Added.Should().Be(1);
        summary.Skipped.Should().Be(1);
        summary.GroupsAdded.Should().Be(1);
        target.Groups.Select(g => g.Name).Should().Equal("A");
    }

    /// <summary>
    /// Verifies an alert is not added when the instrument has an alert with the condition, and a different condition is
    /// added next to it.
    /// </summary>
    [Fact]
    public void Merge_AlertWithTheConditionOfAnExistingAlert_IsSkipped()
    {
        var target = new AppSettings();
        target.Watchlist.Add(new WatchlistEntry { ProviderId = "binance", Symbol = "BTCUSDT", AssetClass = AssetClass.Crypto });
        target.Alerts.Add(new AlertRule { Id = "mine", ProviderId = "binance", Symbol = "BTCUSDT", Condition = AlertCondition.PriceAbove, Threshold = 1 });
        var import = new WatchlistImport();
        import.Entries.Add(new ImportedEntry(new WatchlistEntry { ProviderId = "binance", Symbol = "BTCUSDT", AssetClass = AssetClass.Crypto }, null));
        import.Alerts.Add(new AlertRule { ProviderId = "binance", Symbol = "BTCUSDT", Condition = AlertCondition.PriceAbove, Threshold = 99 });
        import.Alerts.Add(new AlertRule { ProviderId = "binance", Symbol = "BTCUSDT", Condition = AlertCondition.PriceBelow, Threshold = 2 });

        var summary = WatchlistTransfer.Merge(target, import, Providers);

        summary.AlertsAdded.Should().Be(1);
        summary.AlertsSkipped.Should().Be(1);
        target.Alerts.Select(a => (a.Condition, a.Threshold)).Should().Equal((AlertCondition.PriceAbove, 1m), (AlertCondition.PriceBelow, 2m));
    }

    /// <summary>
    /// Verifies a file with nothing new is reported as one that changes nothing.
    /// </summary>
    [Fact]
    public void ChangesNothing_NothingAddedOrFilled_IsTrue()
    {
        new ImportSummary(0, 5, 0, 1, 2, 0, 0, 3).ChangesNothing.Should().BeTrue();
        new ImportSummary(0, 0, 1, 0, 0, 0, 0, 0).ChangesNothing.Should().BeFalse();
    }
}

using System.Text;
using FluxTick.Abstractions;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the export and the import of the watchlist in the settings.
/// </summary>
public class DataTransferViewModelTests
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
    /// Builds settings with both instruments, a holding on Bitcoin and an alert on Apple.
    /// </summary>
    /// <returns>
    /// The settings.
    /// </returns>
    private static AppSettings Sample()
    {
        var settings = Harness.WatchlistOf(Btc, Aapl);
        settings.Watchlist[0].CostPrice = 100m;
        settings.Watchlist[0].Quantity = 2m;
        settings.Alerts.Add(new AlertRule { ProviderId = Aapl.ProviderId, Symbol = Aapl.Symbol, Condition = AlertCondition.PriceAbove, Threshold = 200m });
        return settings;
    }

    /// <summary>
    /// Verifies the JSON export writes the file without a byte order mark and names it after the date.
    /// </summary>
    /// <returns>
    /// A task that completes when the check is done.
    /// </returns>
    [Fact]
    public async Task ExportJson_WritesTheFileAndSaysSo()
    {
        await using var h = new Harness(Sample());

        await h.SettingsPage.Transfer.ExportJsonCommand.ExecuteAsync(null);

        h.FilePicker.SuggestedName.Should().Be("fluxtick-watchlist-20260101.json");
        h.FilePicker.Written.Should().NotBeNull();
        h.FilePicker.Written!.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        var text = Encoding.UTF8.GetString(h.FilePicker.Written!);
        text.Should().Contain(WatchlistTransfer.FormatId).And.Contain("AAPL").And.Contain("200");
        h.SettingsPage.Transfer.HasMessage.Should().BeTrue();
        h.SettingsPage.Transfer.MessageIsError.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the table export starts with a byte order mark, so that a spreadsheet reads it as UTF-8.
    /// </summary>
    /// <returns>
    /// A task that completes when the check is done.
    /// </returns>
    [Fact]
    public async Task ExportCsv_StartsWithAByteOrderMark()
    {
        await using var h = new Harness(Sample());

        await h.SettingsPage.Transfer.ExportCsvCommand.ExecuteAsync(null);

        h.FilePicker.SuggestedName.Should().EndWith(".csv");
        h.FilePicker.Written!.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        Encoding.UTF8.GetString(h.FilePicker.Written!).TrimStart((char)0xFEFF).Should().StartWith("provider,symbol,name,class,group,cost,quantity,currency");
    }

    /// <summary>
    /// Verifies a cancelled dialog writes nothing and says nothing.
    /// </summary>
    /// <returns>
    /// A task that completes when the check is done.
    /// </returns>
    [Fact]
    public async Task Export_Cancelled_WritesNothing()
    {
        await using var h = new Harness(Sample());
        h.FilePicker.CancelWrite = true;

        await h.SettingsPage.Transfer.ExportJsonCommand.ExecuteAsync(null);

        h.FilePicker.Written.Should().BeNull();
        h.SettingsPage.Transfer.HasMessage.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a file that cannot be opened is reported and does not end the app.
    /// </summary>
    /// <returns>
    /// A task that completes when the check is done.
    /// </returns>
    [Fact]
    public async Task Export_FileFails_ShowsAnError()
    {
        await using var h = new Harness(Sample());
        h.FilePicker.Failure = new IOException("disk full");

        await h.SettingsPage.Transfer.ExportJsonCommand.ExecuteAsync(null);

        h.SettingsPage.Transfer.MessageIsError.Should().BeTrue();
        h.SettingsPage.Transfer.HasMessage.Should().BeTrue();
    }

    /// <summary>
    /// Verifies a file that is read is shown as a preview and changes nothing until it is confirmed, and that the
    /// confirmation merges it in.
    /// </summary>
    /// <returns>
    /// A task that completes when the check is done.
    /// </returns>
    [Fact]
    public async Task Import_Json_PreviewsThenMergesOnConfirm()
    {
        await using var source = new Harness(Sample());
        await source.SettingsPage.Transfer.ExportJsonCommand.ExecuteAsync(null);
        await using var h = new Harness(new AppSettings());
        h.FilePicker.ReadBytes = source.FilePicker.Written;
        var transfer = h.SettingsPage.Transfer;

        await transfer.ImportCommand.ExecuteAsync(null);

        transfer.HasPreview.Should().BeTrue();
        transfer.PreviewText.Should().Contain("2");
        h.Settings.Current.Watchlist.Should().BeEmpty("nothing changes before the user agrees");

        await transfer.ConfirmImportCommand.ExecuteAsync(null);

        transfer.HasPreview.Should().BeFalse();
        h.Settings.Current.Watchlist.Select(e => e.Symbol).Should().Equal("BTC", "AAPL");
        h.Settings.Current.Watchlist[0].CostPrice.Should().Be(100m);
        h.Settings.Current.Alerts.Should().ContainSingle().Which.Threshold.Should().Be(200m);
        transfer.HasMessage.Should().BeTrue();
        transfer.MessageIsError.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a table is recognized by its content and read like a JSON file, with a spreadsheet's byte order mark.
    /// </summary>
    /// <returns>
    /// A task that completes when the check is done.
    /// </returns>
    [Fact]
    public async Task Import_Csv_IsReadByItsContent()
    {
        await using var h = new Harness(new AppSettings());
        var csv = $"{(char)0xFEFF}provider,symbol,name,class\r\n{Harness.Crypto},ETH,Ether,Crypto\r\n";
        h.FilePicker.ReadBytes = new UTF8Encoding(false).GetBytes(csv);

        await h.SettingsPage.Transfer.ImportCommand.ExecuteAsync(null);
        await h.SettingsPage.Transfer.ConfirmImportCommand.ExecuteAsync(null);

        h.Settings.Current.Watchlist.Should().ContainSingle().Which.DisplayName.Should().Be("Ether");
    }

    /// <summary>
    /// Verifies a cancelled preview leaves the settings as they were.
    /// </summary>
    /// <returns>
    /// A task that completes when the check is done.
    /// </returns>
    [Fact]
    public async Task CancelImport_ForgetsThePreview()
    {
        await using var h = new Harness(new AppSettings());
        h.FilePicker.ReadBytes = Encoding.UTF8.GetBytes($"provider,symbol,class\r\n{Harness.Crypto},ETH,Crypto\r\n");
        await h.SettingsPage.Transfer.ImportCommand.ExecuteAsync(null);

        h.SettingsPage.Transfer.CancelImportCommand.Execute(null);
        await h.SettingsPage.Transfer.ConfirmImportCommand.ExecuteAsync(null);

        h.SettingsPage.Transfer.HasPreview.Should().BeFalse();
        h.Settings.Current.Watchlist.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies a file that is not one of the app, an empty one and a table without the needed columns are refused with
    /// an error and no preview.
    /// </summary>
    /// <param name="content">
    /// The content of the file.
    /// </param>
    /// <returns>
    /// A task that completes when the check is done.
    /// </returns>
    [Theory]
    [InlineData("{ \"hello\": 1 }")]
    [InlineData("   ")]
    [InlineData("provider,symbol\r\nbinance,BTC\r\n")]
    public async Task Import_UnusableFile_ShowsAnErrorAndNoPreview(string content)
    {
        await using var h = new Harness(new AppSettings());
        h.FilePicker.ReadBytes = Encoding.UTF8.GetBytes(content);

        await h.SettingsPage.Transfer.ImportCommand.ExecuteAsync(null);

        h.SettingsPage.Transfer.HasPreview.Should().BeFalse();
        h.SettingsPage.Transfer.MessageIsError.Should().BeTrue();
    }

    /// <summary>
    /// Verifies a file far larger than a watchlist is not read into memory.
    /// </summary>
    /// <returns>
    /// A task that completes when the check is done.
    /// </returns>
    [Fact]
    public async Task Import_TooLargeFile_IsRefused()
    {
        await using var h = new Harness(new AppSettings());
        h.FilePicker.ReadBytes = new byte[9 * 1024 * 1024];

        await h.SettingsPage.Transfer.ImportCommand.ExecuteAsync(null);

        h.SettingsPage.Transfer.HasPreview.Should().BeFalse();
        h.SettingsPage.Transfer.MessageIsError.Should().BeTrue();
    }

    /// <summary>
    /// Verifies a file with nothing the watchlist lacks says so instead of asking for a confirmation.
    /// </summary>
    /// <returns>
    /// A task that completes when the check is done.
    /// </returns>
    [Fact]
    public async Task Import_NothingNew_SaysSoWithoutAPreview()
    {
        await using var h = new Harness(Sample());
        await h.SettingsPage.Transfer.ExportJsonCommand.ExecuteAsync(null);
        h.FilePicker.ReadBytes = h.FilePicker.Written;

        await h.SettingsPage.Transfer.ImportCommand.ExecuteAsync(null);

        h.SettingsPage.Transfer.HasPreview.Should().BeFalse();
        h.SettingsPage.Transfer.HasMessage.Should().BeTrue();
        h.SettingsPage.Transfer.MessageIsError.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a cancelled dialog and a failing one leave no preview, the first without a message and the second with an
    /// error.
    /// </summary>
    /// <returns>
    /// A task that completes when the check is done.
    /// </returns>
    [Fact]
    public async Task Import_CancelledOrFailing_LeavesNoPreview()
    {
        await using var h = new Harness(new AppSettings());
        var transfer = h.SettingsPage.Transfer;

        await transfer.ImportCommand.ExecuteAsync(null);
        transfer.HasPreview.Should().BeFalse();
        transfer.HasMessage.Should().BeFalse();

        h.FilePicker.Failure = new UnauthorizedAccessException();
        await transfer.ImportCommand.ExecuteAsync(null);

        transfer.HasPreview.Should().BeFalse();
        transfer.MessageIsError.Should().BeTrue();
    }
}

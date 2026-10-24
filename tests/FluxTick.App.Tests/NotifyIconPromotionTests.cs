using FluxTick.App.Services;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for following the tray icon records of Windows.
/// </summary>
public class NotifyIconPromotionTests
{
    /// <summary>
    /// The running executable.
    /// </summary>
    private const string Exe = @"C:\Apps\FluxTick\FluxTick.exe";

    /// <summary>
    /// Verifies the first refresh scans the records, and later ones read only the key of the executable.
    /// </summary>
    [Fact]
    public void Refresh_ReadsOnlyTheOwnKeyOnceItIsKnown()
    {
        var records = new FakeRecords(new NotifyIconSettings.Entry("own", Exe, false), new NotifyIconSettings.Entry("x", @"C:\Other\Other.exe", true));
        var promotion = Create(records);

        promotion.Refresh();
        promotion.Promoted.Should().BeFalse();
        promotion.HasRecord.Should().BeTrue();
        records.Scans.Should().Be(1);

        records.Set("own", true);
        promotion.Refresh();

        promotion.Promoted.Should().BeTrue();
        records.Scans.Should().Be(1);
    }

    /// <summary>
    /// Verifies the records are scanned again when the record of the executable disappears or only appears later.
    /// </summary>
    [Fact]
    public void Refresh_FindsARecordThatAppearsLater()
    {
        var records = new FakeRecords();
        var promotion = Create(records);

        promotion.Refresh();
        promotion.Promoted.Should().BeTrue();
        promotion.HasRecord.Should().BeFalse();

        records.Add(new NotifyIconSettings.Entry("own", Exe, false));
        promotion.Refresh();
        promotion.Promoted.Should().BeFalse();
        promotion.HasRecord.Should().BeTrue();

        records.Remove("own");
        promotion.Refresh();
        promotion.Promoted.Should().BeTrue();
        promotion.HasRecord.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a failing read keeps the last answer.
    /// </summary>
    [Fact]
    public void Refresh_WhenTheRecordsCannotBeRead_KeepsTheLastAnswer()
    {
        var records = new FakeRecords(new NotifyIconSettings.Entry("own", Exe, false));
        var promotion = Create(records);
        promotion.Refresh();

        records.Fail = true;
        promotion.Refresh();

        promotion.Promoted.Should().BeFalse();
    }

    /// <summary>
    /// Verifies only records of other copies whose file is gone are deleted, never the own one nor those of other programs.
    /// </summary>
    [Fact]
    public void DropStale_DeletesOnlyRecordsOfCopiesThatAreGone()
    {
        var gone = @"D:\Old\FluxTick.exe";
        var kept = @"E:\Usb\FluxTick.exe";
        var records = new FakeRecords(
            new NotifyIconSettings.Entry("own", Exe, true),
            new NotifyIconSettings.Entry("gone", gone, false),
            new NotifyIconSettings.Entry("kept", kept, false),
            new NotifyIconSettings.Entry("other", @"D:\Old\Other.exe", false));
        var promotion = Create(records, path => path == gone);

        promotion.DropStale();

        records.Deleted.Should().Equal("gone");
    }

    /// <summary>
    /// Verifies a failing delete does not escape.
    /// </summary>
    [Fact]
    public void DropStale_WhenTheRecordsCannotBeRead_DoesNothing()
    {
        var records = new FakeRecords(new NotifyIconSettings.Entry("gone", @"D:\Old\FluxTick.exe", false)) { Fail = true };
        var promotion = Create(records, _ => true);

        var act = promotion.DropStale;

        act.Should().NotThrow();
        records.Deleted.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies a file counts as gone only when its drive is there.
    /// </summary>
    [Theory]
    [InlineData(@"D:\Old\FluxTick.exe", true, false, true)]
    [InlineData(@"D:\Old\FluxTick.exe", true, true, false)]
    [InlineData(@"E:\Usb\FluxTick.exe", false, false, false)]
    [InlineData(@"\\server\share\FluxTick.exe", true, false, false)]
    [InlineData(@"{905e63b6-c1bf-494e-b29c-65b732d3d21a}\FluxTick\FluxTick.exe", true, false, false)]
    public void IsFileGone_NeedsTheDriveToBeThere(string path, bool driveExists, bool fileExists, bool expected)
    {
        NotifyIconPromotion.IsFileGone(path, _ => fileExists, _ => driveExists).Should().Be(expected);
    }

    /// <summary>
    /// Creates the object under test.
    /// </summary>
    /// <param name="records">
    /// The fake records.
    /// </param>
    /// <param name="isGone">
    /// Tells whether a file is gone; by default none is.
    /// </param>
    /// <returns>
    /// The promotion.
    /// </returns>
    private static NotifyIconPromotion Create(FakeRecords records, Func<string, bool>? isGone = null) =>
        new(records, Exe, _ => null, isGone ?? (_ => false));

    /// <summary>
    /// Records kept in memory.
    /// </summary>
    private sealed class FakeRecords : INotifyIconRecords
    {
        /// <summary>
        /// The records.
        /// </summary>
        private readonly List<NotifyIconSettings.Entry> _entries;

        /// <summary>
        /// Initializes a new instance of the <see cref="FakeRecords"/> class.
        /// </summary>
        /// <param name="entries">
        /// The records.
        /// </param>
        public FakeRecords(params NotifyIconSettings.Entry[] entries) => _entries = [.. entries];

        /// <summary>
        /// Gets or sets a value indicating whether reading fails.
        /// </summary>
        public bool Fail { get; set; }

        /// <summary>
        /// Gets how many times all records were read.
        /// </summary>
        public int Scans { get; private set; }

        /// <summary>
        /// Gets the keys that were deleted.
        /// </summary>
        public List<string> Deleted { get; } = [];

        /// <inheritdoc />
        public IReadOnlyList<NotifyIconSettings.Entry> ReadAll()
        {
            if (Fail)
            {
                throw new IOException("unavailable");
            }

            Scans++;
            return [.. _entries];
        }

        /// <inheritdoc />
        public bool? ReadPromoted(string key)
        {
            if (Fail)
            {
                throw new IOException("unavailable");
            }

            var index = _entries.FindIndex(e => e.Key == key);
            return index < 0 ? null : _entries[index].Promoted;
        }

        /// <inheritdoc />
        public void Delete(string key) => Deleted.Add(key);

        /// <summary>
        /// Adds a record.
        /// </summary>
        /// <param name="entry">
        /// The record.
        /// </param>
        public void Add(NotifyIconSettings.Entry entry) => _entries.Add(entry);

        /// <summary>
        /// Removes a record.
        /// </summary>
        /// <param name="key">
        /// The key of the record.
        /// </param>
        public void Remove(string key) => _entries.RemoveAll(e => e.Key == key);

        /// <summary>
        /// Changes the choice of a record.
        /// </summary>
        /// <param name="key">
        /// The key of the record.
        /// </param>
        /// <param name="promoted">
        /// The new choice.
        /// </param>
        public void Set(string key, bool promoted)
        {
            var index = _entries.FindIndex(e => e.Key == key);
            _entries[index] = _entries[index] with { Promoted = promoted };
        }
    }
}

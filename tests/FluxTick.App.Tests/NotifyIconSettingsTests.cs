using FluxTick.App.Services;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for reading the tray icon records of Windows.
/// </summary>
public class NotifyIconSettingsTests
{
    /// <summary>
    /// The running executable.
    /// </summary>
    private const string Exe = @"C:\Program Files\FluxTick\FluxTick.exe";

    /// <summary>
    /// The id of the Program Files folder.
    /// </summary>
    private static readonly Guid ProgramFiles = new("905e63b6-c1bf-494e-b29c-65b732d3d21a");

    /// <summary>
    /// Finds the only known folder the tests use.
    /// </summary>
    /// <param name="id">
    /// The folder id.
    /// </param>
    /// <returns>
    /// The folder, or <see langword="null"/> for an unknown id.
    /// </returns>
    private static string? Folder(Guid id) => id == ProgramFiles ? @"C:\Program Files" : null;

    /// <summary>
    /// Verifies a known folder id at the start of a path is replaced by the folder, and other paths are left alone.
    /// </summary>
    [Fact]
    public void Normalize_ReplacesKnownFolderId()
    {
        NotifyIconSettings.Normalize(@"{905e63b6-c1bf-494e-b29c-65b732d3d21a}\FluxTick\FluxTick.exe", Folder).Should().Be(Exe);
        NotifyIconSettings.Normalize(@"D:\Tools\FluxTick.exe", Folder).Should().Be(@"D:\Tools\FluxTick.exe");
        NotifyIconSettings.Normalize(@"{00000000-0000-0000-0000-000000000001}\a.exe", Folder).Should().Be(@"{00000000-0000-0000-0000-000000000001}\a.exe");
    }

    /// <summary>
    /// Verifies the record with exactly this path decides, whatever case it is written in.
    /// </summary>
    [Fact]
    public void Resolve_ExactPath_UsesItsChoice()
    {
        var entries = new[] { new NotifyIconSettings.Entry("1", Exe.ToUpperInvariant(), false) };

        var result = NotifyIconSettings.Resolve(entries, Exe, Folder, _ => false);

        result.Promoted.Should().BeFalse();
        result.OwnKey.Should().Be("1");
        result.Stale.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies a record written with a known folder id is found as the record of an installed copy and not dropped.
    /// </summary>
    [Fact]
    public void Resolve_KnownFolderPath_IsTheOwnRecord()
    {
        var entries = new[] { new NotifyIconSettings.Entry("1", @"{905e63b6-c1bf-494e-b29c-65b732d3d21a}\FluxTick\FluxTick.exe", false) };

        var result = NotifyIconSettings.Resolve(entries, Exe, Folder, _ => true);

        result.Promoted.Should().BeFalse();
        result.OwnKey.Should().Be("1");
        result.Stale.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies another copy is ignored while its file exists, and only reported as left over once the file is gone.
    /// </summary>
    [Fact]
    public void Resolve_OtherCopy_IsStaleOnlyWhenItsFileIsGone()
    {
        var other = @"D:\Old\FluxTick.exe";
        var entries = new[] { new NotifyIconSettings.Entry("old", other, false), new NotifyIconSettings.Entry("own", Exe, true) };

        var present = NotifyIconSettings.Resolve(entries, Exe, Folder, _ => false);
        var gone = NotifyIconSettings.Resolve(entries, Exe, Folder, path => path == other);

        present.Promoted.Should().BeTrue();
        present.Stale.Should().BeEmpty();
        gone.Promoted.Should().BeTrue();
        gone.Stale.Should().Equal("old");
    }

    /// <summary>
    /// Verifies records of other programs are left alone and an unknown icon counts as shown.
    /// </summary>
    [Fact]
    public void Resolve_NoRecordOfThisPath_AssumesShownAndKeepsOtherPrograms()
    {
        var entries = new[] { new NotifyIconSettings.Entry("x", @"C:\Apps\Other\Other.exe", false) };

        var result = NotifyIconSettings.Resolve(entries, Exe, Folder, _ => true);

        result.Promoted.Should().BeTrue();
        result.OwnKey.Should().BeNull();
        result.Stale.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies the paths are taken apart as Windows paths whatever system the code runs on.
    /// </summary>
    [Fact]
    public void WindowsPaths_AreTakenApartTheSameEverywhere()
    {
        NotifyIconSettings.Normalize(@"{905e63b6-c1bf-494e-b29c-65b732d3d21a}/FluxTick/FluxTick.exe", Folder).Should().Be(Exe);
        NotifyIconSettings.Resolve(
            [new NotifyIconSettings.Entry("old", @"D:\Old\FluxTick.exe", false)],
            @"C:\Apps\FluxTick.exe",
            Folder,
            _ => true).Stale.Should().Equal("old");
    }
}

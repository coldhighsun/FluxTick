using FluxTick.App.Services;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests of the buttons on the General page that open the settings and the log folder.
/// </summary>
public sealed class OpenFolderCommandTests
{
    /// <summary>
    /// Verifies each button opens its own folder.
    /// </summary>
    [Fact]
    public async Task Commands_Executed_OpenTheirOwnFolder()
    {
        var opened = new List<string>();
        await using var h = new Harness(openFolder: directory =>
        {
            lock (opened)
            {
                opened.Add(directory);
            }

            return true;
        });

        await h.SettingsPage.OpenSettingsFolderCommand.ExecuteAsync(null);
        await h.SettingsPage.OpenLogFolderCommand.ExecuteAsync(null);

        opened.Should().Equal(AppPaths.DataDirectory, AppPaths.LogDirectory);
        h.SettingsPage.FolderOpenFailed.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a failure is shown, and cleared again by the next success.
    /// </summary>
    [Fact]
    public async Task Command_FailsThenSucceeds_ShowsAndClearsTheFailure()
    {
        var works = false;
        await using var h = new Harness(openFolder: _ => works);

        await h.SettingsPage.OpenLogFolderCommand.ExecuteAsync(null);
        h.SettingsPage.FolderOpenFailed.Should().BeTrue();

        works = true;
        await h.SettingsPage.OpenLogFolderCommand.ExecuteAsync(null);
        h.SettingsPage.FolderOpenFailed.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a slow, earlier click cannot overwrite the outcome of a later one.
    /// </summary>
    [Fact]
    public async Task Commands_Overlap_ShowOnlyTheLatestOutcome()
    {
        var release = new TaskCompletionSource();
        var firstStarted = new TaskCompletionSource();
        await using var h = new Harness(openFolder: directory =>
        {
            if (directory == AppPaths.DataDirectory)
            {
                firstStarted.SetResult();
                release.Task.Wait();
                return false;
            }

            return true;
        });

        var slowFailure = h.SettingsPage.OpenSettingsFolderCommand.ExecuteAsync(null);
        await firstStarted.Task;
        await h.SettingsPage.OpenLogFolderCommand.ExecuteAsync(null);
        release.SetResult();
        await slowFailure;

        h.SettingsPage.FolderOpenFailed.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a defect in opening the folder is shown as a failure and does not end the app.
    /// </summary>
    [Fact]
    public async Task Command_OpenerThrows_ShowsFailureInsteadOfFaulting()
    {
        await using var h = new Harness(openFolder: _ => throw new InvalidOperationException("defect"));

        await h.SettingsPage.OpenLogFolderCommand.ExecuteAsync(null);

        h.SettingsPage.FolderOpenFailed.Should().BeTrue();
    }

    /// <summary>
    /// Verifies a failure is forgotten when the page is opened again.
    /// </summary>
    [Fact]
    public async Task Reload_AfterAFailure_ClearsIt()
    {
        await using var h = new Harness(openFolder: _ => false);
        await h.SettingsPage.OpenSettingsFolderCommand.ExecuteAsync(null);
        h.SettingsPage.FolderOpenFailed.Should().BeTrue();

        h.SettingsPage.Reload();

        h.SettingsPage.FolderOpenFailed.Should().BeFalse();
    }
}

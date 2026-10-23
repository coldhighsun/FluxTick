using AwesomeAssertions;
using FluxTick.App.Services;
using FluxTick.Core.Application;

namespace FluxTick.App.Tests;

/// <summary>
/// An <see cref="IAutoStartService"/> that remembers its state instead of touching the system.
/// </summary>
public sealed class FakeAutoStartService : IAutoStartService
{
    /// <summary>
    /// Gets or sets a value indicating whether the entry exists.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether registering fails.
    /// </summary>
    public bool Fails { get; set; }

    /// <inheritdoc />
    public bool IsSupported => true;

    /// <inheritdoc />
    public bool IsEnabled() => Enabled;

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        if (Fails)
        {
            throw new UnauthorizedAccessException();
        }

        Enabled = enabled;
    }

    /// <inheritdoc />
    public void RepairIfMoved()
    {
    }
}

/// <summary>
/// Tests of the start at sign-in setting.
/// </summary>
public sealed class AutoStartTests
{
    /// <summary>
    /// Verifies switching it on registers the app, and nothing about it is written to the settings file.
    /// </summary>
    [Fact]
    public async Task LaunchAtLogin_SwitchedOn_RegistersWithoutSaving()
    {
        var autoStart = new FakeAutoStartService();
        await using var h = new Harness(autoStart: autoStart);
        var saved = h.Store.Stored;

        h.SettingsPage.LaunchAtLogin = true;

        autoStart.Enabled.Should().BeTrue();
        h.Store.Stored.Should().BeSameAs(saved);
    }

    /// <summary>
    /// Verifies switching it off removes the registration.
    /// </summary>
    [Fact]
    public async Task LaunchAtLogin_SwitchedOff_Unregisters()
    {
        var autoStart = new FakeAutoStartService { Enabled = true };
        await using var h = new Harness(autoStart: autoStart);
        h.SettingsPage.LaunchAtLogin.Should().BeTrue();

        h.SettingsPage.LaunchAtLogin = false;

        autoStart.Enabled.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the page follows the system's entry when it is loaded again.
    /// </summary>
    [Fact]
    public async Task Reload_EntryChangedBySystem_FollowsIt()
    {
        var autoStart = new FakeAutoStartService { Enabled = true };
        await using var h = new Harness(autoStart: autoStart);

        autoStart.Enabled = false;
        h.SettingsPage.Reload();

        h.SettingsPage.LaunchAtLogin.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the switch goes back when the system refuses.
    /// </summary>
    [Fact]
    public async Task LaunchAtLogin_RegistrationFails_RevertsSwitch()
    {
        var autoStart = new FakeAutoStartService { Fails = true };
        await using var h = new Harness(autoStart: autoStart);

        h.SettingsPage.LaunchAtLogin = true;

        h.SettingsPage.LaunchAtLogin.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the switch is unavailable when the platform has no support.
    /// </summary>
    [Fact]
    public async Task IsAutoStartSupported_NoService_IsFalse()
    {
        await using var h = new Harness();

        h.SettingsPage.IsAutoStartSupported.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the executable is registered, and a dotnet host is not.
    /// </summary>
    /// <param name="path">
    /// The process path.
    /// </param>
    /// <param name="expected">
    /// Whether the path can be registered.
    /// </param>
    [Theory]
    [InlineData(@"C:\Apps\FluxTick\FluxTick.exe", true)]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe", false)]
    [InlineData(@"C:\Program Files\dotnet\DOTNET.EXE", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsApplicationPath_Path_RejectsHostAndEmpty(string? path, bool expected) =>
        AutoStartCommand.IsApplicationPath(path).Should().Be(expected);

    /// <summary>
    /// Verifies the executable is read out of quoted and unquoted command lines.
    /// </summary>
    /// <param name="command">
    /// The registered command line.
    /// </param>
    /// <param name="expected">
    /// The executable path.
    /// </param>
    [Theory]
    [InlineData("\"C:\\My Apps\\FluxTick.exe\"", "C:\\My Apps\\FluxTick.exe")]
    [InlineData("\"C:\\My Apps\\FluxTick.exe\" --minimized", "C:\\My Apps\\FluxTick.exe")]
    [InlineData("C:\\Apps\\FluxTick.exe --minimized", "C:\\Apps\\FluxTick.exe")]
    [InlineData("C:\\Apps\\FluxTick.exe", "C:\\Apps\\FluxTick.exe")]
    [InlineData("  ", null)]
    public void ExecutableOf_CommandLine_ReadsPath(string command, string? expected) =>
        AutoStartCommand.ExecutableOf(command).Should().Be(expected);

    /// <summary>
    /// Verifies the registered command quotes a path with spaces.
    /// </summary>
    [Fact]
    public void Build_PathWithSpaces_IsQuoted() =>
        AutoStartCommand.Build(@"C:\My Apps\FluxTick.exe").Should().Be("\"C:\\My Apps\\FluxTick.exe\"");
}

/// <summary>
/// Tests of the tray settings.
/// </summary>
public sealed class TrayPolicyTests
{
    /// <summary>
    /// Verifies the defaults show the window at startup and hide it on close.
    /// </summary>
    [Fact]
    public void Defaults_ShowWindowAtStartAndHideOnClose()
    {
        var general = new AppSettings().General;

        TrayPolicy.StartHidden(general, [], hasTrayIcon: () => true).Should().BeFalse();
        TrayPolicy.CloseHidesWindow(general, hasTrayIcon: () => true).Should().BeTrue();
    }

    /// <summary>
    /// Verifies the setting and the command line option each keep the window hidden at startup.
    /// </summary>
    [Fact]
    public void StartHidden_SettingOrOption_IsTrue()
    {
        var general = new GeneralSettings { StartMinimized = true };

        TrayPolicy.StartHidden(general, [], hasTrayIcon: () => true).Should().BeTrue();
        TrayPolicy.StartHidden(new GeneralSettings(), ["--MINIMIZED"], hasTrayIcon: () => true).Should().BeTrue();
    }

    /// <summary>
    /// Verifies the window is shown at startup when there is no tray icon to bring it back.
    /// </summary>
    [Fact]
    public void StartHidden_NoTrayIcon_IsFalse() =>
        TrayPolicy.StartHidden(new GeneralSettings { StartMinimized = true }, ["--minimized"], hasTrayIcon: () => false).Should().BeFalse();

    /// <summary>
    /// Verifies closing hides the window only when the user wants that and there is a tray to bring it back from.
    /// </summary>
    [Fact]
    public void CloseHidesWindow_NeedsSettingAndTray()
    {
        TrayPolicy.CloseHidesWindow(new GeneralSettings { CloseToTray = true }, hasTrayIcon: () => true).Should().BeTrue();
        TrayPolicy.CloseHidesWindow(new GeneralSettings { CloseToTray = false }, hasTrayIcon: () => true).Should().BeFalse();
        TrayPolicy.CloseHidesWindow(new GeneralSettings { CloseToTray = true }, hasTrayIcon: () => false).Should().BeFalse();
    }

    /// <summary>
    /// Verifies the tray is not looked for when neither hiding at startup nor hiding on close is wanted.
    /// </summary>
    [Fact]
    public void TrayProbe_NotWanted_IsNotAsked()
    {
        var asked = false;
        bool Probe()
        {
            asked = true;
            return true;
        }

        TrayPolicy.StartHidden(new GeneralSettings(), [], Probe);
        TrayPolicy.CloseHidesWindow(new GeneralSettings { CloseToTray = false }, Probe);

        asked.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the reply of the session bus is read.
    /// </summary>
    /// <param name="output">
    /// The text printed by <c>dbus-send --print-reply</c>.
    /// </param>
    /// <param name="expected">
    /// Whether the name has an owner.
    /// </param>
    [Theory]
    [InlineData("method return time=1.5 sender=:1.4 -> destination=:1.99 serial=3 reply_serial=2\n   boolean true\n", true)]
    [InlineData("method return time=1.5 sender=:1.4 -> destination=:1.99 serial=3 reply_serial=2\r\n   boolean false\r\n", false)]
    [InlineData("", false)]
    [InlineData("Error org.freedesktop.DBus.Error.ServiceUnknown", false)]
    public void ParseNameHasOwner_Reply_ReadsLastLine(string output, bool expected) =>
        TrayAvailability.ParseNameHasOwner(output).Should().Be(expected);

    /// <summary>
    /// Verifies the tray settings are persisted and loaded back into the page.
    /// </summary>
    [Fact]
    public async Task Settings_Toggled_PersistAndReload()
    {
        await using var h = new Harness();

        h.SettingsPage.StartMinimized = true;
        h.SettingsPage.CloseToTray = false;

        await Harness.EventuallyAsync(() => h.Store.Stored!.General is { StartMinimized: true, CloseToTray: false });
        h.SettingsPage.Reload();
        h.SettingsPage.StartMinimized.Should().BeTrue();
        h.SettingsPage.CloseToTray.Should().BeFalse();
    }
}

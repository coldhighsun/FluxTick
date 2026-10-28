using System.ComponentModel;
using System.Diagnostics;
using FluxTick.App.Services;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests of <see cref="ExternalLinks"/>, which decides what the app may open in the browser.
/// </summary>
public sealed class ExternalLinksTests
{
    /// <summary>
    /// Verifies only https addresses on github.com are accepted.
    /// </summary>
    /// <param name="url">
    /// The address.
    /// </param>
    /// <param name="expected">
    /// Whether it may be opened.
    /// </param>
    [Theory]
    [InlineData("https://github.com/coldhighsun/FluxTick/releases/tag/v1.0.0", true)]
    [InlineData("https://GitHub.com/coldhighsun/FluxTick", true)]
    [InlineData("http://github.com/coldhighsun/FluxTick", false)]
    [InlineData("https://github.com.evil.example/x", false)]
    [InlineData("https://evilgithub.com/x", false)]
    [InlineData("https://user@github.com/x", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("ms-msdt:/id", false)]
    [InlineData(@"C:\Windows\System32\calc.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsGitHubUrl_Address_AcceptsOnlyHttpsGitHub(string? url, bool expected) =>
        ExternalLinks.IsGitHubUrl(url).Should().Be(expected);

    /// <summary>
    /// Verifies an accepted address is handed to the shell and a refused one never is.
    /// </summary>
    [Fact]
    public void TryOpenGitHub_AcceptedAndRefusedAddress_StartsOnlyTheAccepted()
    {
        var started = new List<ProcessStartInfo>();

        var opened = ExternalLinks.TryOpenGitHub("https://github.com/coldhighsun/FluxTick", started.Add);
        var refused = ExternalLinks.TryOpenGitHub("file:///C:/Windows/System32/calc.exe", started.Add);

        opened.Should().BeTrue();
        refused.Should().BeFalse();
        started.Should().ContainSingle();
        started[0].FileName.Should().Be("https://github.com/coldhighsun/FluxTick");
        started[0].UseShellExecute.Should().BeTrue();
    }

    /// <summary>
    /// Verifies a machine without a browser is reported as not opened instead of failing.
    /// </summary>
    [Fact]
    public void TryOpenGitHub_NoBrowser_ReturnsFalse()
    {
        var opened = ExternalLinks.TryOpenGitHub(
            "https://github.com/coldhighsun/FluxTick",
            _ => throw new Win32Exception("no application is associated"));

        opened.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a web page is accepted for https only, and never with credentials in the address.
    /// </summary>
    /// <param name="url">
    /// The address.
    /// </param>
    /// <param name="expected">
    /// Whether it may be opened.
    /// </param>
    [Theory]
    [InlineData("https://finance.yahoo.com/", true)]
    [InlineData("http://gu.qq.com/", false)]
    [InlineData("https://user:password@example.test/", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("ms-msdt:/id", false)]
    [InlineData(@"C:\Windows\System32\calc.exe", false)]
    [InlineData("finance.yahoo.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsWebUrl_Address_AcceptsOnlyHttps(string? url, bool expected) =>
        ExternalLinks.IsWebUrl(url).Should().Be(expected);

    /// <summary>
    /// Verifies a web page is handed to the shell and a refused address never is.
    /// </summary>
    [Fact]
    public void TryOpenWebsite_AcceptedAndRefusedAddress_StartsOnlyTheAccepted()
    {
        var started = new List<ProcessStartInfo>();

        var opened = ExternalLinks.TryOpenWebsite("https://finance.yahoo.com/", started.Add);
        var refused = ExternalLinks.TryOpenWebsite("file:///C:/Windows/System32/calc.exe", started.Add);

        opened.Should().BeTrue();
        refused.Should().BeFalse();
        started.Should().ContainSingle();
        started[0].FileName.Should().Be("https://finance.yahoo.com/");
        started[0].UseShellExecute.Should().BeTrue();
    }
}

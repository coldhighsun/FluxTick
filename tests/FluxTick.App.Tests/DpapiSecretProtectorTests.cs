using System.Runtime.Versioning;
using FluxTick.App.Services;

namespace FluxTick.App.Tests;

/// <summary>
/// Tests for the protector that uses the Windows Data Protection API; they run on Windows only.
/// </summary>
[SupportedOSPlatform("windows")]
public class DpapiSecretProtectorTests
{
    /// <summary>
    /// Gets a value indicating whether the tests can run: only Windows has the API.
    /// </summary>
    public static bool IsWindows => OperatingSystem.IsWindows();

    /// <summary>
    /// Verifies a value comes back as it was and is not readable in the protected text.
    /// </summary>
    [Fact(SkipUnless = nameof(IsWindows), SkipType = typeof(DpapiSecretProtectorTests), Skip = "Windows only")]
    public void Protect_ThenTryUnprotect_RoundTrips()
    {
        var protector = new DpapiSecretProtector();

        var text = protector.Protect("eu_prod_secret_key", "eulerpool:ApiKey");

        text.Should().NotContain("secret").And.NotBeEmpty();
        protector.TryUnprotect(text, "eulerpool:ApiKey", out var plain).Should().BeTrue();
        plain.Should().Be("eu_prod_secret_key");
    }

    /// <summary>
    /// Verifies the protected text does not open for another option.
    /// </summary>
    [Fact(SkipUnless = nameof(IsWindows), SkipType = typeof(DpapiSecretProtectorTests), Skip = "Windows only")]
    public void TryUnprotect_OtherPurpose_Fails()
    {
        var protector = new DpapiSecretProtector();
        var text = protector.Protect("value", "eulerpool:ApiKey");

        protector.TryUnprotect(text, "yahoo:ApiKey", out var plain).Should().BeFalse();
        plain.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies damaged text is reported as not opened, not as an exception.
    /// </summary>
    [Theory(SkipUnless = nameof(IsWindows), SkipType = typeof(DpapiSecretProtectorTests), Skip = "Windows only")]
    [InlineData("")]
    [InlineData("not base64 !!")]
    [InlineData("AAAA")]
    public void TryUnprotect_DamagedText_Fails(string text) =>
        new DpapiSecretProtector().TryUnprotect(text, "eulerpool:ApiKey", out _).Should().BeFalse();
}

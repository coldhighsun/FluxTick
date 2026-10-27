using FluxTick.Providers.Common;

namespace FluxTick.Providers.Tests;

/// <summary>
/// Tests for the HTTP clients of the app.
/// </summary>
public class AppHttpClientTests
{
    /// <summary>
    /// Verifies that a client has the common timeout and sends the user agent it was given.
    /// </summary>
    [Fact]
    public void Create_WithUserAgent_SetsTimeoutAndUserAgent()
    {
        using var http = AppHttpClient.Create("FluxTick");

        http.Timeout.Should().Be(TimeSpan.FromSeconds(15));
        http.DefaultRequestHeaders.UserAgent.ToString().Should().Be("FluxTick");
    }

    /// <summary>
    /// Verifies that a client sends no user agent unless one is given.
    /// </summary>
    [Fact]
    public void Create_WithoutUserAgent_SendsNone()
    {
        using var http = AppHttpClient.Create(decompress: true, connectionLifetime: TimeSpan.FromMinutes(5));

        http.DefaultRequestHeaders.UserAgent.Should().BeEmpty();
    }
}

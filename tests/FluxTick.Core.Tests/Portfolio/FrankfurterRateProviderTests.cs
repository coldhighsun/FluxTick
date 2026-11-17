using System.Net;
using FluxTick.Core.Application;
using FluxTick.Core.Infrastructure;

namespace FluxTick.Core.Tests;

/// <summary>
/// Tests for <see cref="FrankfurterRateProvider"/>.
/// </summary>
public class FrankfurterRateProviderTests
{
    /// <summary>
    /// Verifies that the rates of the answer are returned.
    /// </summary>
    [Fact]
    public async Task GetRatesAsync_ValidAnswer_ReturnsRates()
    {
        using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, """{"base":"USD","date":"2026-01-02","rates":{"CNY":7.1,"EUR":0.9}}"""));
        var provider = new FrankfurterRateProvider(http);

        var rates = await provider.GetRatesAsync(CancellationToken.None);

        rates["CNY"].Should().Be(7.1m);
        rates["EUR"].Should().Be(0.9m);
    }

    /// <summary>
    /// Verifies that an answer without rates is an error.
    /// </summary>
    [Fact]
    public async Task GetRatesAsync_EmptyRates_Throws()
    {
        using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, """{"base":"USD","rates":{}}"""));
        var provider = new FrankfurterRateProvider(http);

        var act = () => provider.GetRatesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ExchangeRateException>();
    }

    /// <summary>
    /// Verifies that a failing service is an error.
    /// </summary>
    [Fact]
    public async Task GetRatesAsync_ServerError_Throws()
    {
        using var http = new HttpClient(new StubHandler(HttpStatusCode.InternalServerError, "oops"));
        var provider = new FrankfurterRateProvider(http);

        var act = () => provider.GetRatesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    /// <summary>
    /// Verifies that the provider disposes the client it owns.
    /// </summary>
    [Fact]
    public async Task Dispose_ProviderOwnsTheClient_ClientCannotBeUsedAnyMore()
    {
        var http = new HttpClient(new StubHandler(HttpStatusCode.OK, "{}"));
        var provider = new FrankfurterRateProvider(http);

        provider.Dispose();

        var act = () => http.GetAsync("https://example.invalid/");
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    /// <summary>
    /// A handler that answers every request the same way.
    /// </summary>
    /// <param name="status">
    /// The status code.
    /// </param>
    /// <param name="body">
    /// The body.
    /// </param>
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}

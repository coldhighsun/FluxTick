using System.Net;
using FluxTick.Abstractions;
using FluxTick.Providers.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

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

    /// <summary>
    /// Verifies that a successful request is logged with its method, host, path and status, and without its query, which
    /// may carry an API key.
    /// </summary>
    [Fact]
    public async Task SendAsync_Success_LogsRequestWithoutQuery()
    {
        var logger = new ListLogger();
        using var http = new HttpClient(new LoggingHttpHandler(new StubHandler(HttpStatusCode.OK), logger));

        using var response = await http.GetAsync(new Uri("https://api.example.com/v1/quote?apikey=SECRET"));

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Debug);
        entry.Message.Should().Contain("GET").And.Contain("api.example.com/v1/quote").And.Contain("200");
        entry.Message.Should().NotContain("SECRET").And.NotContain("apikey");
    }

    /// <summary>
    /// Verifies that an answer other than success is logged as a warning.
    /// </summary>
    [Fact]
    public async Task SendAsync_ErrorStatus_LogsWarning()
    {
        var logger = new ListLogger();
        using var http = new HttpClient(new LoggingHttpHandler(new StubHandler(HttpStatusCode.TooManyRequests), logger));

        using var response = await http.GetAsync(new Uri("https://api.example.com/v1/quote"));

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("429");
    }

    /// <summary>
    /// Verifies that a transition of the status of a provider is logged as information, and a change of the detail alone
    /// as debug.
    /// </summary>
    [Fact]
    public void SetStatus_Transition_IsLoggedAtInformationAndDetailChangeAtDebug()
    {
        var logger = new ListLogger();
        using var provider = new StatusProvider(logger);

        provider.Change(ProviderConnectionState.Reconnecting, "Connecting");
        provider.Change(ProviderConnectionState.Reconnecting, "Retry 2");

        logger.Entries.Should().HaveCount(2);
        logger.Entries[0].Level.Should().Be(LogLevel.Information);
        logger.Entries[0].Message.Should().Contain("Idle").And.Contain("Reconnecting").And.Contain("Connecting");
        logger.Entries[1].Level.Should().Be(LogLevel.Debug);
    }

    /// <summary>
    /// A handler that answers every request with a status.
    /// </summary>
    /// <param name="status">
    /// The status of the answer.
    /// </param>
    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    /// <summary>
    /// A provider that only has a status to change.
    /// </summary>
    /// <param name="logger">
    /// The logger that receives the transitions.
    /// </param>
    private sealed class StatusProvider(ILogger logger)
        : LoggingProviderBase(
            new ProviderDescriptor("test", "Test", "A test source.", [AssetClass.Equity], ProviderUpdateMode.Polling),
            new FakeTimeProvider(),
            logger),
        IDisposable
    {
        /// <summary>
        /// Changes the status.
        /// </summary>
        /// <param name="state">
        /// The new state.
        /// </param>
        /// <param name="message">
        /// The detail.
        /// </param>
        public void Change(ProviderConnectionState state, string message) => SetStatus(state, message);

        /// <inheritdoc />
        void IDisposable.Dispose()
        {
        }

        /// <inheritdoc />
        protected override Task OnSubscriptionsChangedAsync(
            IReadOnlyCollection<Asset> current,
            IReadOnlyCollection<Asset> added,
            IReadOnlyCollection<Asset> removed,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

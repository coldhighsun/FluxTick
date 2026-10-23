# Adding a data source

FluxTick treats every market data source as a plug-in-shaped class library that only depends on
`FluxTick.Abstractions`. Adding one never requires changes to `FluxTick.Core` or the UI: the app discovers
the provider from its descriptor, lists it under **Settings → Data sources**, and renders its option editors
from the option definitions you declare.

## 1. Create the project

```bash
dotnet new classlib -n FluxTick.Providers.MySource -o src/FluxTick.Providers.MySource
dotnet sln FluxTick.slnx add src/FluxTick.Providers.MySource
```

Reference only the abstractions:

```xml
<ItemGroup>
  <ProjectReference Include="..\FluxTick.Abstractions\FluxTick.Abstractions.csproj" />
</ItemGroup>
```

## 2. Implement the provider

Derive from `MarketDataProviderBase`. It owns the quote channel, status reporting, subscription diffing and
disposal. You implement one method: react to subscription changes, and call `Publish(quote)` whenever you have
a price.

```csharp
public sealed class MySourceProvider(ProviderDescriptor descriptor, TimeSpan interval, TimeProvider time)
    : MarketDataProviderBase(descriptor, time)
{
    private ITimer? _timer;

    protected override Task OnSubscriptionsChangedAsync(
        IReadOnlyCollection<Asset> current,
        IReadOnlyCollection<Asset> added,
        IReadOnlyCollection<Asset> removed,
        CancellationToken cancellationToken)
    {
        if (current.Count == 0)
        {
            _timer?.Dispose();
            _timer = null;
            SetStatus(ProviderConnectionState.Idle);
        }
        else if (_timer is null)
        {
            _timer = TimeProvider.CreateTimer(_ => Poll(), null, TimeSpan.Zero, interval);
            SetStatus(ProviderConnectionState.Connected);
        }

        return Task.CompletedTask;
    }

    private void Poll()
    {
        foreach (var asset in CurrentSubscriptions)
        {
            var price = /* call your API */ 42m;
            Publish(new Quote(asset.Id, price, Reference: 40m, TimeProvider.GetUtcNow()));
        }
    }

    protected override ValueTask DisposeCoreAsync()
    {
        _timer?.Dispose();
        return ValueTask.CompletedTask;
    }
}
```

Rules of thumb:

- Do no network I/O in the constructor or in the factory; start lazily on the first subscription.
- Report problems with `SetStatus(ProviderConnectionState.Reconnecting | Failed, message)` so the UI can show them.
- Observe `ShutdownToken` in every background loop.
- Prefer `TimeProvider` over `DateTime.UtcNow` / `Task.Delay` so the provider is testable with `FakeTimeProvider`.

### Shared helpers

Sources that need more than the base class can reference `FluxTick.Providers.Common` (it only depends on the
abstractions):

| Type | Use it for |
| --- | --- |
| `CryptoExchangeProvider` | A crypto exchange with a ticker WebSocket and REST for history and the pair list. It adds history, symbol search and disposal on top of `WebSocketProviderBase`; you describe the wire formats (Binance, OKX, Coinbase, Kraken and Bybit are short examples). `ExchangeEndpoints` declares and reads the two endpoint options for it. |
| `WebSocketProviderBase` | Any streaming source. It owns connecting, subscribing each instrument exactly once, an optional keep-alive message and reconnecting with exponential backoff. A message that fails to parse is dropped without costing the connection. You implement the subscribe/unsubscribe messages and `HandleMessage`. Override `SendTimeout` (default 10 s; a send that takes longer drops the connection and reconnects), `MaxMessageBytes` (default 4 MB; a larger single message drops the connection) or `HealthyUptime` (default 10 s; only a connection that stayed up this long restarts the backoff at `InitialBackoff`) when an exchange needs other limits. |
| `PollingProviderBase` | Any polling source (Yahoo and Tencent). It owns the loop, an immediate poll for newly added instruments, the delay that doubles while the service pushes back, and the Connected/Reconnecting/Failed status. You implement one method that fetches and publishes, and return a `PollOutcome`; an exception you let escape counts as a failed cycle. |
| `SymbolIndex` / `SymbolSearch` | Searching a downloaded pair listing for crypto exchanges. Build a `SymbolIndex` once per download (`CryptoExchangeProvider` keeps it in an `ExpiringCache`) and call `SymbolSearch.Search` with it for every query; the index holds the normalized names so a search does not rebuild them. |
| `ExpiringCache<T>` | Keeping a downloaded symbol list for a few minutes; concurrent callers share one download. |
| `EndpointOptions.GetWebSocketUri` / `GetHttpUri` / `Combine` | Reading a URL option and falling back to its default when the stored text is not an address with the right scheme (`ws`/`wss` or `http`/`https`); use these instead of `Uri.TryCreate`, which also accepts text such as `host:8443/path`. `Combine` appends a request path to an endpoint and keeps the endpoint's own path (a proxy prefix), which `new Uri(baseUri, "/path")` would replace. |
| `JsonValues` | Reading decimals, Unix timestamps and ISO 8601 times that an API sends as either numbers or strings. |

Watch out for symbols that are case-sensitive or carry a prefix. `MarketDataProviderBase.Publish` drops a quote whose
`AssetId` is not exactly one of the subscribed ones, so a source that normalizes symbols before calling its API must
publish under the symbol that was subscribed (see `TencentProvider`).

## 3. Optional capabilities

Implement these interfaces on the provider class when your source supports them. Missing capabilities degrade
gracefully instead of failing.

| Interface | Gives the app |
| --- | --- |
| `IHistoryProvider` | Recent prices to seed the sparkline immediately |
| `ISymbolSearchProvider` | Symbol search and validation in the *Add instrument* page |

Without `ISymbolSearchProvider` the *Add instrument* page lets the user type the symbol as it is.

## 4. Describe it with a factory

```csharp
public sealed class MySourceProviderFactory : IMarketDataProviderFactory
{
    public ProviderDescriptor Descriptor { get; } = new(
        Id: "mysource",                       // stable, lower-case; stored in settings, never rename
        DisplayName: "My Source",
        Description: "What it covers and any caveats.",
        SupportedAssetClasses: [AssetClass.Equity, AssetClass.Index],
        UpdateMode: ProviderUpdateMode.Polling,
        Options:
        [
            new ProviderOptionDefinition("PollInterval", "Poll interval", ProviderOptionType.Duration,
                "00:00:10", "How often prices are refreshed.", Min: 2, Max: 300),
        ]);

    public IMarketDataProvider Create(ProviderCreationContext context) =>
        new MySourceProvider(Descriptor, context.Options.GetDuration("PollInterval"), context.TimeProvider);
}
```

Option types: `String`, `Secret`, `Int`, `Bool`, `Duration`, `Choice`. The settings page generates the right editor
and validates input from the definition. `ProviderOptions` always returns a valid value, falling back to the default
for anything missing or malformed.

How the settings page stores options, so you know what your declaration promises:

- An option that equals its declared `DefaultValue` is **not written** to `settings.json`. If you change a default in a
  later version, it reaches every user who never customised that option, and only those. Files written by earlier
  builds, which stored every option explicitly, are cleaned up when the app starts: stored values that are valid and
  equal the default are removed. Invalid values are left in place (they are ignored at run time).
- `Int`, `Bool` and `Duration` values are compared by meaning, not by text (`05` and `5`, `True` and `true`, or
  `00:00:05` and `00:00:05.0000000` are the same value), so rewriting such a value in another form never restarts a
  running provider. `String`, `Secret` and `Choice` values are compared as exact text.
- A stored value that does not validate is treated as the default, in the provider and in the editor alike.
- Each data source that declares options has a **Restore defaults** button that puts its options back to their declared
  defaults. `Secret` options (keys, tokens) are left alone, because they are credentials rather than settings.

## 5. Register it

In `src/FluxTick.App/Services/AppHost.cs`:

```csharp
services.AddMarketDataProvider<MySourceProviderFactory>();
```

and add a project reference from `FluxTick.App`. That's the whole integration.

## 6. Test it

Add a two-line contract test; it checks descriptor sanity, option fallback, idle-without-subscriptions and
prompt disposal for free:

```csharp
public sealed class MySourceProviderContractTests : ProviderContractTests<MySourceProviderFactory>;
```

Then add provider-specific tests: keep parsing logic in pure functions and test them with captured payloads, and
use a stub `HttpMessageHandler` plus `FakeTimeProvider` for polling behavior (see `YahooTests` for an example).

## Not supported yet

- Loading providers from DLLs at runtime (a `plugins/` folder). The contract is designed for it; it is a follow-up.
- Automatic failover to the next provider in the routing list.
- Secure storage for `Secret` options: values are currently stored in `settings.json` in plain text.

# Adding a data source

FluxTick treats every market data source as a plug-in-shaped class library that only depends on
`FluxTick.Abstractions`. Adding one never requires changes to `FluxTick.Core` or the UI code: the app discovers
the provider from its descriptor, lists it under **Settings → Data sources**, and renders its option editors
from the option definitions you declare. Only the Chinese translation of its texts is optional extra work
(see [step 5](#5-register-it)).

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

The last argument of `Quote` is **when the price was traded**, not when your code received or parsed it. The app
compares it with the clock: a quote older than the stale threshold (one minute by default) makes the instrument show as `Delayed` (quotes keep
arriving, as a polled source does after the market closed or a delayed feed does; it is `Stale` when nothing arrives at all, or when the quote is older than 30 minutes, see `DelayedUpTo` in `MarketDataService`), and the sparkline places the price at that time, so a
stretch without trades shows as a gray dashed line. Use the time the API reports; fall back to
`TimeProvider.GetUtcNow()` only when it reports none.

Because of that time, quotes may arrive in any order: the app ignores a quote that was traded before the one it shows
(only one whose time you took from the clock is exempt, as it is always the newest). This lets a source mix a stream with
a REST snapshot or fallback without the late REST answer moving the price back. Quotes with the same time replace each
other.

Rules of thumb:

- Do no network I/O in the constructor or in the factory; start lazily on the first subscription.
- Expect empty and non-empty subscription sets to alternate. A provider gets an empty set while the watchlist has no instrument for it, and its instruments again when the user adds some. On an empty set close the connection, stop timers and report `Idle`; on the next non-empty set start from scratch, as on the first one. The base classes do this for you.
- Report problems with `SetStatus(ProviderConnectionState.Reconnecting | Failed, message)` so the UI can show them. While you report `Reconnecting` the instruments show as reconnecting; once nothing has been received for the stale threshold (one minute by default) they show as `Stale · reconnecting`, their holdings no longer count in the portfolio total, and price alerts, which are only checked on live prices, wait. A listener of `StatusChanged` that throws is swallowed and passed to `OnStatusListenerFailed` (the polling and WebSocket base classes log it), so a broken listener never ends your loop.
- Do not go silent for a market you know is shut. The app reads a source from which nothing has arrived for the stale threshold as a quiet one, and then no longer knows the trading state of its instruments: a stock that showed as closed turns stale. If you skip the requests for a shut market (see `TradingHoursPolicy`), publish its latest quote again with the trade time it had, about once per poll interval, which costs no request and moves nothing on screen. `PollingProviderBase` and `RestBackedWebSocketProviderBase` do this for you through `QuoteRepeater`; a source that derives from `MarketDataProviderBase` directly has to do it itself.
- Observe `ShutdownToken` in every background loop.
- Prefer `TimeProvider` over `DateTime.UtcNow` / `Task.Delay` so the provider is testable with `FakeTimeProvider`.

### Shared helpers

Sources that need more than the base class can reference `FluxTick.Providers.Common` (it only depends on the
abstractions):

| Type | Use it for |
| --- | --- |
| `CryptoExchangeProvider` | A crypto exchange with a ticker WebSocket and REST for history and the pair list. It adds history, symbol search and disposal on top of `WebSocketProviderBase`; you describe the wire formats (Binance, OKX, Coinbase, Kraken and Bybit are short examples). `ExchangeEndpoints` declares and reads the two endpoint options for it. |
| `WebSocketProviderBase` | Any streaming source. It owns connecting, subscribing each instrument exactly once, an optional keep-alive message and reconnecting with exponential backoff. A message that fails to parse is dropped without costing the connection. You implement the subscribe/unsubscribe messages and `HandleMessage`. Override `SendTimeout` (default 10 s; a send that takes longer drops the connection and reconnects), `MaxMessageBytes` (default 4 MB; a larger single message drops the connection) or `HealthyUptime` (default 10 s; only a connection that stayed up this long restarts the backoff at `InitialBackoff`) when an exchange needs other limits. Two hooks cover servers with special needs: `ConfigureSocket` sets request headers of the handshake (Sina insists on an `Origin`), and `SubscribesByUri` puts the instruments in the address instead of messages (`BuildConnectionUri` builds it; a change of the watchlist then connects again after `ReconnectDebounce`, without a failure status or a backoff). |
| `RestBackedWebSocketProviderBase` | A streaming source that also has a REST service (Yahoo and Sina). On top of `WebSocketProviderBase` it fetches the first price of every new instrument over REST, because a stream is silent for a market that is closed, and polls REST while the stream is not connected, doubling the wait while the service pushes back. A stream that is connected but quiet (a closed market is quiet too, so this is slow) is polled once for every `QuietStreamInterval` (one minute by default), which also refreshes market states. If three such rounds in a row find newer prices while the stream delivers none (`QuietRoundsBeforeReconnect`; only a message that publishes a quote counts as the stream delivering, so acknowledgements and heartbeats do not, and `HandleMessage` must publish synchronously), the connection is dropped and opened again. Only prices that data should be arriving for right now count: a recent trade, newer than the last one seen, of an instrument already known and of a market the source does not report as closed, so a closed market never counts. A reconnect that does not help makes the next one wait twice as many rounds (up to eight times), until the stream says something. While the stream is down its status follows what REST delivers: prices that keep arriving report the source as connected (`AdjustStatus` is the hook), three rounds in a row without a price report it as failed, and a problem REST reports, such as a symbol without data, shows in the status message. You implement one method, `FetchQuotesAsync`, that fetches and publishes quotes. |
| `PollingProviderBase` | Any polling source (Tencent), when the data source has no stream. It owns the loop, an immediate poll for newly added instruments, the delay that doubles while the service pushes back, the Connected/Reconnecting/Failed status, and the skipping of shut markets, whose latest quote it repeats every poll interval, whatever the push back does to the delay. You implement one method that fetches and publishes, and return a `PollOutcome` (set its `Problems` to tell which instrument each problem is about, so that it goes away when the instrument is removed); an exception you let escape counts as a failed cycle. |
| `BatchQuotePollingProviderBase` / `GbkText` | A polling source whose service answers many codes in one request, as Tencent and Sina do (`BatchQuoteFetcher` does the grouping and batching, so a streaming source can use it for its REST requests too). It groups subscriptions by code, requests them 50 at a time, publishes each answered line under the subscribed symbol and reports codes without data. You implement `ToRequestCode` and `RequestQuotesAsync` (build the URL, parse the answer into `ParsedQuote`s). `GbkText.Decode` reads the GBK bodies of those services. |
| `SymbolIndex` / `SymbolSearch` | Searching a downloaded pair listing for crypto exchanges. Build a `SymbolIndex` once per download (`CryptoExchangeProvider` keeps it in an `ExpiringCache`) and call `SymbolSearch.Search` with it for every query; the index holds the normalized names so a search does not rebuild them. |
| `ExpiringCache<T>` | Keeping a downloaded symbol list for a few minutes; concurrent callers share one download. When a reload fails and an old value exists, the old value is served and the next try waits a minute; pass the optional third argument to log that. |
| `EndpointOptions.GetWebSocketUri` / `GetHttpUri` / `Combine` | Reading a URL option and falling back to its default when the stored text is not an address with the right scheme (`ws`/`wss` or `http`/`https`); use these instead of `Uri.TryCreate`, which also accepts text such as `host:8443/path`. `Combine` appends a request path to an endpoint and keeps the endpoint's own path (a proxy prefix), which `new Uri(baseUri, "/path")` would replace. |
| `Publish(asset, …)` / `PublishToSubscribers(key, …)` / `KeyOf` | Publishing a price that a message names by a key (a symbol, or a code in another form). `PublishToSubscribers` finds the subscribed instruments of the key without regard to case and publishes under the symbol each was subscribed with; the base class keeps the index, so there is nothing to maintain. A quote without a trade time is stamped with the clock and marked as estimated. Override `KeyOf` when the service names instruments differently from how they are stored (Sina lower-cases stocks and upper-cases futures). |
| `JsonValues` | Reading decimals, Unix timestamps and ISO 8601 times that an API sends as either numbers or strings. |

Watch out for symbols that are case-sensitive or carry a prefix. `MarketDataProviderBase.Publish` drops a quote whose
`AssetId` is not exactly one of the subscribed ones, so a source that normalizes symbols before calling its API must
publish under the symbol that was subscribed (see `TencentProvider`).

## 3. Optional capabilities

Implement these interfaces on the provider class when your source supports them. Missing capabilities degrade
gracefully instead of failing.

| Interface | Gives the app |
| --- | --- |
| `IHistoryProvider` | Recent prices to seed the sparkline immediately. The sparkline shows the last 60 minutes only, so ask for about the last hour (`HistoryWindow.Span` in `Providers.Common`) and not for a whole day |
| `ISymbolSearchProvider` | Symbol search and validation in the *Add instrument* page |
| `IMarketHoursProvider` | Open / closed / pre-market / break state of the markets of your symbols; for stocks and indices, whose hours depend on your symbol format (use `ExchangeHours` from `Providers.Common`) |
| `IQuoteCadenceProvider` | How often the source polls (and tops up a quiet stream), so a quiet instrument is shown delayed rather than stale until that time has passed. `PollingProviderBase` and `RestBackedWebSocketProviderBase` implement it; a source on neither base that polls should too |

A source built on `RestBackedWebSocketProviderBase` or `PollingProviderBase` that also implements `IMarketHoursProvider` does not fetch prices of a market that is not trading (rule in `TradingHoursPolicy`): a streaming source polls only while its markets are open (or unknown), because the stream delivers the pre-market and after hours; a polling source skips a shut market and the midday break. The first price of a new instrument is always fetched. While the stream of a streaming source is down and no market is open, its status is Connected with a note instead of Reconnecting. Offer the `QuietPollInterval` option (`ProviderOptionDefinition.QuietPollIntervalKey`, default 5 minutes) so the user can choose how often a connected but silent stream is topped up.

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

Optional descriptor arguments:

- `EnabledByDefault: false` keeps the source off on first run, so the user turns it on in **Settings → Data sources**
  (Tencent, OKX and Simulated do this). Use it for demo sources and for sources that only suit some users.
- `RequiresApiKey` (read-only) is true when the source has a `Required` option, such as a key.
- `Homepage` is the official site of the data source (an https address). The settings page shows it as a link on the card of the source, so the user can open it; leave it out only for a source that makes up its data.

Option types: `String`, `Secret`, `Int`, `Bool`, `Duration`, `Choice`. The settings page generates the right editor
and validates input from the definition. `ProviderOptions` always returns a valid value, falling back to the default
for anything missing or malformed. A `Duration` must be greater than zero (a poll interval of zero would spin), whatever
`Min` and `Max` you declare, so do not use zero as a default.

How the settings page stores options, so you know what your declaration promises:

- An option that equals its declared `DefaultValue` is **not written** to `settings.json`. If you change a default in a
  later version, it reaches every user who never customised that option, and only those. Files written by earlier
  builds, which stored every option explicitly, are cleaned up when the app starts: stored values that are valid and
  equal the default are removed. Invalid values are left in place (they are ignored at run time).
- `Int`, `Bool` and `Duration` values are compared by meaning, not by text (`05` and `5`, `True` and `true`, or
  `00:00:05` and `00:00:05.0000000` are the same value), so rewriting such a value in another form never restarts a
  running provider. `String`, `Secret` and `Choice` values are compared as exact text.
- A stored value that does not validate is treated as the default, in the provider and in the editor alike.
- Edits to the options of a source are a draft: the card shows **Save** and **Revert** buttons, and nothing reaches
  `settings.json` until the user saves, so a running provider restarts once per save, not once per keystroke. Saving
  checks every option and refuses invalid values. The enable switch takes effect at once. An edit that was not saved is
  kept when the settings window is closed and opened again.
- A `String` or `Secret` option declared with `Required: true` (an API key, say) must not be blank while the source is
  on. A source that has no saved value for one can not be switched on: the switch goes back, and the card opens on the
  missing field. A source that is switched on in `settings.json` but lacks one (for example after an update adds it) is
  not started: the settings page opens and flags it, and `AppSettings.IsProviderEnabled` is false for it.
- Each data source that declares options has a **Restore defaults** button that puts the editors of its options back to
  their declared defaults, as a draft the user then saves. `Secret` options (keys, tokens) are left alone, because they are
  credentials rather than settings.

### How `Secret` values are stored

A `Secret` option is never written to `settings.json` as typed on Windows: `ProtectedSettingsStore` protects it with the Windows Data Protection API (current user, bound to `{providerId}:{Key}`) as the file is written and opens it as the file is read, so the settings manager, the providers and the settings page all work with the plain value. On macOS and Linux there is no built-in protection yet, so the value is stored as it is and the settings file is limited to its owner (mode 600). A protected value that cannot be opened (another user or machine) is not given to the settings, so the source asks for the key again; the text stays in the file, unchanged, until the user enters a new one or switches the source off, so a value that cannot be opened now is not lost by saving something else. Copies of the settings file made by earlier versions (such as `settings.json.bad`) may still hold the key as it was typed; the log says so after the first protected save, and they can be deleted. Your source needs to do nothing for this; just declare the option as `Secret`, and do not log it or store it anywhere else.

## 5. Register it

In `src/FluxTick.App/Services/AppHost.cs`:

```csharp
services.AddMarketDataProvider<MySourceProviderFactory>();
```

and add a project reference from `FluxTick.App`. That's the whole integration.

### Translations (optional)

The provider describes itself in English. The interface language setting can show it in Chinese when
`src/FluxTick.App/Localization/Strings/zh-CN.json` has these keys (`ProviderText` looks them up; anything missing
falls back to the English text from your descriptor):

| Key | Translates |
| --- | --- |
| `Provider_{id}_Name` | `DisplayName` |
| `Provider_{id}_Desc` | `Description` |
| `Option_{Key}_Label` | The label of an option, shared by every provider |
| `Option_{id}_{Key}_Label` | The label of an option for your provider only; used before the shared one |
| `Option_{id}_{Key}_Desc` | The description of an option |

An option that reuses a key such as `PollInterval` gets the shared translation. If it means something else for your
source (for the streaming ones it is the interval while the stream is down), add `Option_{id}_{Key}_Label` instead of
inventing a new key. `en.json` needs no
entries, and the localization tests only allow keys starting with `Provider_` or `Option_` that exist in `zh-CN.json` alone.

## 6. Test it

Add a project reference from `tests/FluxTick.Providers.Tests/FluxTick.Providers.Tests.csproj` to your project. If the
tests need your `internal` parsing functions, also add `<InternalsVisibleTo Include="FluxTick.Providers.Tests" />`
to your own project file (see `FluxTick.Providers.Yahoo`).

Then add a two-line contract test; it checks descriptor sanity, option fallback, idle-without-subscriptions and prompt
disposal for free (the restart after an empty set is covered for the shared base classes; test it yourself if you
implement `IMarketDataProvider` directly):

```csharp
public sealed class MySourceProviderContractTests : ProviderContractTests<MySourceProviderFactory>;
```

Then add provider-specific tests: keep parsing logic in pure functions and test them with captured payloads, and
use a stub `HttpMessageHandler` plus `FakeTimeProvider` for polling behavior (see `YahooTests` for an example).

## Not supported yet

- Loading providers from DLLs at runtime (a `plugins/` folder). The contract is designed for it; it is a follow-up.
- Automatic failover to the next provider in the order of the data sources.
- Secure storage for `Secret` options on macOS and Linux (Keychain, libsecret). The protection sits behind `ISecretProtector`, so a platform implementation can be added without touching the sources.

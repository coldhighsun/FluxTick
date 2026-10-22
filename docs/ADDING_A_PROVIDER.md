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

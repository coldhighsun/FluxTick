# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

The SDK is pinned in `global.json` (.NET 10) and tests use xUnit v3 on Microsoft.Testing.Platform, so the `dotnet test` flags differ from the VSTest ones.

```bash
dotnet build FluxTick.slnx
dotnet test --solution FluxTick.slnx                       # CI runs the same with -c Release
dotnet test --project tests/FluxTick.Core.Tests            # one test project
dotnet test --project tests/FluxTick.Core.Tests --filter-method "*AlertEvaluatorTests.*"   # xUnit v3 filters (--filter-class, --filter-method, --filter-trait)
dotnet run --project src/FluxTick.App                      # run the app
dotnet run --project src/FluxTick.App -- --demo            # offline, simulated data (also uses a separate single-instance name)
```

- `Directory.Build.props` sets `UseArtifactsOutput`; a local, untracked `Directory.Build.user` may redirect `ArtifactsPath`, so build output is not under `bin/` next to the projects.
- `GenerateDocumentationFile` is on and `.editorconfig` makes file-scoped namespaces and braces warnings; every member carries an XML doc comment.

## Architecture

Layering is enforced by project references:

- `FluxTick.Abstractions` — the only thing a data source references: `IMarketDataProvider`, `MarketDataProviderBase` (quote channel, status, subscription diffing, disposal), `ProviderDescriptor`/`ProviderOptionDefinition`, `Quote`, `Asset`.
- `FluxTick.Core` — provider registry/router, `MarketDataService` (quote aggregation, stale detection), alerts, holdings P/L and exchange rates (`Frankfurter`), `SettingsManager` + JSON store, `SparklineSeries`. No UI dependency.
- `FluxTick.Providers.Common` — shared bases: `WebSocketProviderBase`, `PollingProviderBase`, `BatchQuotePollingProviderBase`, `CryptoExchangeProvider`, symbol search and parsing helpers.
- `FluxTick.Providers.*` — one project per source (Binance, Okx, Coinbase, Kraken, Bybit, Yahoo, Tencent, Sina, Eulerpool, Simulated), each = Provider + ProviderFactory (+ messages/parser).
- `FluxTick.App` — Avalonia desktop app and composition root (`Services/AppHost.cs`). Adding a data source is a project reference plus one `services.AddMarketDataProvider<TFactory>()` line there; nothing in Core or the UI changes. Full recipe, option semantics and contract tests: `docs/ADDING_A_PROVIDER.md`.

Things that span several files:

- **Provider contract.** `Quote`'s timestamp is the trade time, not receive time; the app uses it for `Delayed` status (`Stale` means nothing was received, or the quote is older than 30 minutes, see `DelayedUpTo` in `MarketDataService`; a quote whose `Market` is `PreMarket` or `AfterHours` is `Live` whatever its age, as long as the source keeps answering and the trading hours do not say the session is over, because few trades make an old trade time there, so report these states only when the source itself knows them; how long a source may stay silent comes from `IQuoteCadenceProvider`, which the polling and REST-backed bases implement) and for placing sparkline points. `Publish` drops quotes whose `AssetId` is not exactly a subscribed id, so providers must publish under the subscribed symbol. Providers must start lazily (no I/O in ctor/factory) and go `Idle` on an empty subscription set. A source that is silent for longer than the stale threshold is read as quiet and its instruments lose their trading state (a closed stock turns `Stale`), so a source that skips requests for a shut market must publish its latest quote again with its original trade time; `PollingProviderBase` and `RestBackedWebSocketProviderBase` do it through `QuoteRepeater`, and `MarketDataService` treats a quote equal to the one shown as received only.
- **Provider options** are declared on the descriptor; the settings UI is generated from them. Values equal to their default are not written to `settings.json` (`ProviderOptionCleanup` strips them at startup), so changing a default reaches users who never customised it.
- **Localization.** `App/Localization/Strings/en.json` and `zh-CN.json` are string tables looked up via `Loc` / the `{T ...}` markup extension; `LocalizationTests` fail if an English key lacks a Chinese one. Provider names/option labels use `Provider_{id}_Name`, `Option_{Key}_Label`, etc., which exist in `zh-CN.json` only.
- **UI logic is split into pure "Math"/"Rules" classes** (`CardFlowMath`, `EdgeDockRules`, `WindowHeightRules`, `TrayClickInterpreter`, ...) next to the views, so layout/behavior can be unit-tested without a window; view models live in `ViewModels/`. Compiled bindings are on by default (`AvaloniaUseCompiledBindingsByDefault`).
- **Platform-specific pieces** (Windows tray popup, global boss-key hotkey, autostart, installer-based updater) are Windows-only and gated by availability classes (`TrayAvailability`, `TrayPopupAvailability`); the app must still run on Linux/macOS.
- Settings and logs live in `%APPDATA%\FluxTick` (`~/.config/FluxTick` elsewhere).

## Tests

`tests/` has `FluxTick.Core.Tests`, `FluxTick.Providers.Tests` (a new provider adds a project reference and a contract test there) and `FluxTick.App.Tests`. In the App tests the whole assembly is one xUnit collection (`AssemblyInfo.cs`) because tests switch the shared `Loc` language; do not enable parallelism there. `XamlStyleRulesTests` reads the `.axaml` sources from disk and is skipped if they are not next to the build output.

## Quote providers

- Design for future data sources. Put functionality shared by sources in a base class (`MarketDataProviderBase`, or the helpers in `FluxTick.Providers.Common`); leave source-specific behavior that does not extract cleanly to each source's own project. Do not add source-specific branches to `FluxTick.Core` or the UI.

- A source that needs a required setting such as an API key (a `Required` option) must be off by default: set `EnabledByDefault: false` in its descriptor, so a fresh install does not enable a source that cannot work until the user fills the setting in.
- Trading hours of stock exchanges depend on a source's symbols, so they live in the source: implement `IMarketHoursProvider` and map your symbols to `ExchangeHours` (shared exchanges and the session logic are in `FluxTick.Providers.Common`). `MarketCalendar` in Core only holds rules that are the same for every source (crypto is always open, the forex week).
- For assets whose trading hours can be determined, judge by the clock first: when the hours say the market is shut (closed or midday break), the UI shows it as closed even if quotes keep arriving and look live. Many sources stamp a repeated closing price with their own clock, so quote freshness alone must not decide it. Where a source's timestamp is its clock rather than the trade time, date the quote to the end of the last session (`ExchangeHours.ClampToSessionEnd`).
- History requests (`IHistoryProvider`) must stay within what the sparkline shows: the last 60 minutes at one point per minute. Ask for about the last hour by time (`HistoryWindow.Span` in `FluxTick.Providers.Common`, 65 minutes) at one-minute resolution, never for a whole day or the service's default count. Use the source's time parameters (`start`/`end`, `since`, `period1`/`period2`, `from`/`to`) or `limit`; if a source has no way to narrow the answer, say so in a comment.
- `Secret` options (API keys) are protected where the settings file is written (`ProtectedSettingsStore`, DPAPI on Windows); the rest of the app sees plain values. Do not store them anywhere else and never log them or put them in a URL.
- Prefer a push transport (WebSocket or another streaming API) to fetch quotes. Fall back to polling only when the data source offers no streaming option.
- When a source offers both, use the stream for live prices and polling only for what the stream does not carry (for example history or a snapshot on connect), or as a fallback while the stream is down.

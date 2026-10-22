# FluxTick

**Every market. Every tick.**

A lightweight, open-source desktop widget for tracking multiple financial markets at a glance.
Real-time numbers, tiny sparklines and a floating always-on-top window. Stocks, crypto, forex, indices and
commodities side by side, from the data sources *you* choose.

<p align="center">
  <img src="docs/images/main.png" alt="FluxTick watchlist" width="300" />
  <img src="docs/images/settings.png" alt="FluxTick settings" width="300" />
</p>

> Screenshots use the built-in simulated data source (`--demo`).

## Features

- **Multi-asset watchlist**: crypto, equities & ETFs, indices, forex and commodities in one list.
- **Live prices**: tick flash on every change, 60-minute sparklines, change vs. previous close / 24h open.
- **Floating widget**: borderless, translucent, always on top, draggable, resizable, lockable; hides to the tray.
- **You pick the data source**: enable or disable sources, tune their options and choose the default source per
  asset class in **Settings**. Move any instrument to another source with **Change data source…**.
- **Honest about data quality**: rows show `Stale`, `Reconnecting`, `Source off` instead of silently showing old prices.
- **Up/down colors**: green-up or red-up, automatic by UI language (red-up for Chinese).
- **Cross-platform**: Windows, Linux and macOS (built on [Avalonia](https://avaloniaui.net/) and .NET 10).
- **Extensible**: adding a data source is one small class library and one line of registration.
  See [docs/ADDING_A_PROVIDER.md](docs/ADDING_A_PROVIDER.md).

## Built-in data sources

| Source | Covers | Mode | Notes |
| --- | --- | --- | --- |
| **Binance** | Crypto spot pairs | WebSocket stream (~1 s) | No API key. If `binance.com` is blocked on your network, set the endpoints to `wss://data-stream.binance.vision/ws` and `https://data-api.binance.vision` in Settings. |
| **Yahoo Finance** | Stocks, ETFs, indices, forex, futures | Polling (default 5 s) | Unofficial API; may be rate limited, delayed or change without notice. No API key. |
| **Simulated** | Anything | Local random walk | For demos, screenshots and offline development. Off by default. |

> Market data may be delayed or inaccurate and is **not investment advice**. FluxTick is not affiliated with any of
> the data providers. Respect each provider's terms of use.

## Getting started

### Run from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/FluxTick.App
```

Try it without network access:

```bash
dotnet run --project src/FluxTick.App -- --demo
```

### Build a single-file executable

```bash
dotnet publish src/FluxTick.App -c Release -r win-x64 -o publish
```

Use `linux-x64`, `osx-arm64` and so on for other platforms. The result is a self-contained `FluxTick` /
`FluxTick.exe`.

### Use it

| Action | How |
| --- | --- |
| Move the window | Drag the header (unless *Lock position* is on) |
| Resize | Drag the bottom-right grip |
| Add an instrument | **+** in the header, pick an asset class and a data source, search |
| Reorder, remove, change source | Right-click a row |
| Compact mode | Double-click the header, or toggle in Settings |
| Hide / show | **×** hides to the tray; click the tray icon to bring it back; *Exit* in the tray menu quits |

Settings and logs live in `%APPDATA%\FluxTick` on Windows and `~/.config/FluxTick` on Linux and macOS.
Delete `settings.json` to reset.

## Project layout

```
src/FluxTick.Abstractions        Stable provider contracts (the only thing a data source references)
src/FluxTick.Core                Provider registry, routing, quote aggregation, sparkline series, settings
src/FluxTick.Providers.Binance   Binance WebSocket source
src/FluxTick.Providers.Yahoo     Yahoo Finance polling source
src/FluxTick.Providers.Simulated Deterministic demo source
src/FluxTick.App                 Avalonia desktop app (FluxTick.exe), composition root
tests/                           xUnit v3 + AwesomeAssertions
```

```bash
dotnet build FluxTick.slnx
dotnet test --solution FluxTick.slnx
```

## Contributing

Issues and pull requests are welcome. Please add or update tests with your change. New data sources are the most
useful contribution; start with [docs/ADDING_A_PROVIDER.md](docs/ADDING_A_PROVIDER.md).

## License

[MIT](LICENSE)

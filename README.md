# FluxTick

[![CI](https://github.com/coldhighsun/FluxTick/actions/workflows/ci.yml/badge.svg)](https://github.com/coldhighsun/FluxTick/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/coldhighsun/FluxTick?include_prereleases&sort=semver)](https://github.com/coldhighsun/FluxTick/releases)
[![License: MIT](https://img.shields.io/github/license/coldhighsun/FluxTick)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download)
[![Avalonia](https://img.shields.io/badge/UI-Avalonia-8B44AC)](https://avaloniaui.net/)
[![Platforms](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-lightgrey)](#getting-started)

**English** | [简体中文](README.zh-CN.md)

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
| **Tencent Finance** | A-shares, Hong Kong and US stocks, ETFs, indices | Polling (default 5 s) | Unofficial API; may be delayed or change without notice. No API key. Search by name, code or pinyin. Minute history (the sparkline backfill) covers A-shares and Hong Kong only; US stocks fill their sparkline from live ticks. Off by default. |
| **OKX** | Crypto spot pairs | WebSocket stream | No API key. Off by default. |
| **Coinbase** | Crypto spot pairs | WebSocket stream | Coinbase Exchange. No API key. Off by default. |
| **Kraken** | Crypto spot pairs | WebSocket stream | No API key. Off by default. |
| **Bybit** | Crypto spot pairs | WebSocket stream | No API key. Not served in some regions. Off by default. |
| **Simulated** | Anything | Local random walk | For demos, screenshots and offline development. Off by default. |

Sources that are off by default are enabled in **Settings → Data sources**. To show an instrument from one of them,
pick it in the *Add instrument* window or make it the default for an asset class on the *Routing* tab.

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
| Add an instrument | **+** in the header opens a window; pick an asset class and a data source, search |
| Reorder | Drag a row up or down |
| Remove, change source | Right-click a row |
| Move the Settings / Add window | Drag its header; **Esc** closes it |
| Settings | The sliders icon in the header opens a window with the tabs *Appearance*, *Data sources* and *Routing*; expand a data source to edit its options |
| Restore a data source | *Restore defaults* in its expanded card puts its options back to the built-in values (secret values such as keys are kept) |
| Compact mode | Double-click the header, or toggle in Settings |
| Hide / show | **×** hides to the tray; click the tray icon to bring it back; *Exit* in the tray menu quits |

Settings and logs live in `%APPDATA%\FluxTick` on Windows and `~/.config/FluxTick` on Linux and macOS.
Options that are at their built-in default are not written to `settings.json`. Delete it to reset everything.

## Project layout

```
src/FluxTick.Abstractions        Stable provider contracts (the only thing a data source references)
src/FluxTick.Core                Provider registry, routing, quote aggregation, sparkline series, settings
src/FluxTick.Providers.Common    Helpers shared by data sources (WebSocket loop, symbol search)
src/FluxTick.Providers.Binance   Binance WebSocket source
src/FluxTick.Providers.Okx       OKX WebSocket source
src/FluxTick.Providers.Coinbase  Coinbase WebSocket source
src/FluxTick.Providers.Kraken    Kraken WebSocket source
src/FluxTick.Providers.Bybit     Bybit WebSocket source
src/FluxTick.Providers.Yahoo     Yahoo Finance polling source
src/FluxTick.Providers.Tencent   Tencent Finance polling source
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

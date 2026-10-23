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
  <img src="docs/images/main.png" alt="FluxTick watchlist" width="250" />
  <img src="docs/images/settings.png" alt="FluxTick settings" width="250" />
  <img src="docs/images/add.png" alt="FluxTick add instrument" width="250" />
</p>

> Screenshots use the built-in simulated data source (`--demo`).

## Features

- **Multi-asset watchlist**: crypto, equities & ETFs, indices, forex and commodities in one list.
- **List or cards**: switch the watchlist to fixed-size cards that flow onto as many columns as the window is wide.
  Group the cards by asset class, by data source or into your own groups; every group collapses on its own, and
  cards can be dragged to reorder them or to move them between your groups. The layout and the grouping switch
  from the header, and new groups are created right where you are working.
- **Live prices**: optional tick flash on every change (off by default, turn it on in Settings), 60-minute sparklines, change vs. previous close / 24h open.
- **Floating widget**: borderless, translucent, always on top, draggable, resizable, lockable; can hide at a screen edge and hides to the tray.
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
| Move the window | Drag the header (unless *Lock position* is on). The header's arrow-to-a-bar button keeps the window always on top; the padlock button locks its position |
| Resize | Drag the bottom-right grip. In the list layout the width is fixed (340 px, or 260 px in compact mode) and only the height changes; in the card layout the width is free and remembered |
| Add an instrument | **+** in the header, or *Add instrument…* in the right-click menu of the empty area, opens a window; pick an asset class and a data source, search |
| Reorder | Drag a row up or down |
| Remove, change source | Right-click a row |
| Move the Settings / Add window | Drag its header; **Esc** closes it |
| Settings | The sliders icon in the header opens a window with the tabs *Appearance*, *Data sources* and *Routing*; expand a data source to edit its options |
| Restore a data source | *Restore defaults* in its expanded card puts its options back to the built-in values (secret values such as keys are kept) |
| Data source status | The footer shows one dot per data source in use (green connected, orange off or failing, grey idle or reconnecting). Sources with a problem come first; when not all fit, a **+N** chip appears. Click the footer to list every source with its state and error message |
| Compact mode | Double-click the header, use *Compact* in the right-click menu of the empty area, or toggle in Settings |
| Card layout | The grid button in the header switches between list and cards (also in the right-click menu of the empty area, and in Settings → Appearance → *Layout*). *Compact* makes the cards smaller by hiding their charts |
| Group cards | In card layout the header's grouping button (or the right-click menu of the empty area, or Settings → Appearance → *Card grouping*) picks *No grouping*, *By type*, *By data source* or *Custom groups*; click a group header to collapse or expand it, press and drag a type or custom group header to reorder the groups |
| Custom groups | A *Default* group exists from the start and new instruments join the first group. Create a group from the grouping button → *New group…*, from the right-click menu of the empty area, from a card's right-click menu (*Move to new group…* puts that card into it), or from the dashed *New group* placeholder below the last group, which also accepts a dragged card. Double-click a group header to rename it, or right-click it for *Rename…* and *Delete group*; a group can be deleted there only while it has no instruments and is not the last group (an empty group also shows a *Delete group* link). Creating a group while another grouping is active switches to *Custom groups*; an *Undo* hint offers to switch back. Reorder groups by pressing and dragging them in Settings → Groups, where you can also delete them (there, deleting a group moves its instruments to the first remaining one); right-click a card → *Move to group*, or drag the card onto a group |
| Price flash | Off by default; turn *Flash on price change* on in the right-click menu of the empty area, or in Settings → Appearance |
| Edge auto-hide | On by default; turn *Auto-hide at screen edge* off in Settings → Appearance (it needs the always-on-top setting). Drop the window at the left, right or top edge of the screen and it slides away, leaving a thin strip; point at the strip to bring it back, move away and it hides again |
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

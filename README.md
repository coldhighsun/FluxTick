# FluxTick

[![CI](https://github.com/coldhighsun/FluxTick/actions/workflows/ci.yml/badge.svg)](https://github.com/coldhighsun/FluxTick/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/coldhighsun/FluxTick?sort=semver&label=release)](https://github.com/coldhighsun/FluxTick/releases/latest)
[![Pre-release](https://img.shields.io/github/v/release/coldhighsun/FluxTick?include_prereleases&filter=*-*&sort=semver&label=pre-release)](https://github.com/coldhighsun/FluxTick/releases)
[![Downloads](https://img.shields.io/github/downloads/coldhighsun/FluxTick/total)](https://github.com/coldhighsun/FluxTick/releases)
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
  <img src="docs/images/en/main-list.png" alt="FluxTick watchlist (list layout)" width="250" />
  <img src="docs/images/en/main-cards.png" alt="FluxTick watchlist (card layout, dark theme)" width="300" />
</p>
<p align="center">
  <img src="docs/images/en/group-custom.png" alt="Custom groups" width="250" />
  <img src="docs/images/en/alerts.png" alt="FluxTick price alerts" width="300" />
</p>

> Screenshots use the built-in simulated data source (`--demo`).

## Features

- **Multi-asset watchlist**: crypto, equities & ETFs, indices, forex and commodities in one list.
- **List or cards**: switch the watchlist to cards that flow onto as many columns as the window is wide. Group them
  by asset class, by data source or into your own groups; groups collapse individually, and cards can be dragged to
  reorder them or move them between groups.
- **Live prices**: 60-minute sparklines drawn along the time (a dashed line where no price came in), change vs.
  previous close / 24h open, and an optional rolling animation of the price number (off by default).
- **Price alerts**: right-click an instrument and choose **Price alerts…** to be told when its price rises or falls to
  a level. An alert fires once (or every time with **Repeat**), shows a small notification, makes the card or row
  flash, and keeps working while the window is hidden.
- **Holdings and profit/loss**: right-click an instrument and choose **Holding cost…** to enter cost price, quantity
  and currency. The profit or loss is shown on the row, card, tooltip and tray popup, and the total over all holdings
  in your base currency (Settings → General). Exchange rates come from the free [Frankfurter](https://frankfurter.dev)
  service (ECB reference rates) and are refreshed hourly. Holdings with a stale price or a failed source are left out
  of the total, and the total says how many.
- **Floating widget**: borderless, translucent, always on top, draggable, resizable, lockable; can hide at a screen edge and hides to the tray.
- **You pick the data source**: enable or disable sources, tune their options and choose the default source per
  asset class in **Settings**. Move any instrument to another source with **Change data source…**.
- **Honest about data quality**: rows show `Stale`, `Delayed`, `Reconnecting` or `Source off` instead of silently
  showing old prices. A price is `Delayed` when quotes keep coming in but were traded longer ago than the stale
  threshold (one minute by default), up to 30 minutes (a delayed feed). It is `Stale` when nothing came in for the
  threshold, or when what comes in was traded more than 30 minutes ago (a stuck source, a closed market). Stale
  prices are left out of the holdings total unless the market is shut; delayed ones still count. Price alerts wait while a source is stale,
  delayed or reconnecting.
- **Language**: Simplified Chinese or English, switchable at runtime; Auto follows the system language.
- **Up/down colors**: green-up or red-up, automatic by UI language (red-up for Chinese).
- **Cross-platform**: Windows, Linux and macOS (built on [Avalonia](https://avaloniaui.net/) and .NET 10).
- **Extensible**: adding a data source is one small class library and one line of registration.
  See [docs/ADDING_A_PROVIDER.md](docs/ADDING_A_PROVIDER.md).

## Built-in data sources

| Source | Covers | Mode | Notes |
| --- | --- | --- | --- |
| **Binance** | Crypto spot pairs | WebSocket stream (~1 s) | No API key. If `binance.com` is blocked, set the endpoints to `wss://data-stream.binance.vision/ws` and `https://data-api.binance.vision` in Settings. |
| **Yahoo Finance** | Stocks, ETFs, indices, forex, futures, crypto | WebSocket stream | Unofficial API; may be rate limited or change without notice. No API key. **Chinese A-share and Hong Kong prices are 15 minutes old**, so they show as `Delayed`; use Sina or Tencent for live A-share prices. Falls back to polling the chart API (default 5 s) while the stream is down. |
| **Tencent Finance** | A-shares, Hong Kong and US stocks, ETFs, indices | Polling (default 5 s) | Unofficial API; may be delayed or change without notice. No API key. Search by name, code or pinyin. Sparkline history covers A-shares and Hong Kong only. Off by default. |
| **Sina Finance** | A-shares, ETFs, indices, currency pairs, international commodity futures | WebSocket stream | Unofficial API; may be rate limited, delayed or change without notice. No API key. Symbols match Tencent's (`sh600000`), but an instrument stays with the source it was added from. Search finds stocks, main indices, and common currency pairs and futures; type ETF codes, and other pairs (`fx_seurusd`) or futures (`hf_GC`) as Sina's codes. Sparkline history covers A-shares, funds and currency pairs. Off by default. |
| **Eulerpool** | Stocks and ETFs worldwide (tickers with an exchange suffix, e.g. `AAPL`, `SAP.DE`) | WebSocket stream | Needs an API key (free at eulerpool.com/developers; see [API keys](#api-keys)). A free key only gets delayed one-second prices; live trades need a paid plan. Search by name, ticker or ISIN. Off by default. |
| **OKX** | Crypto spot pairs | WebSocket stream | No API key. Off by default. |
| **Coinbase** | Crypto spot pairs | WebSocket stream | Coinbase Exchange. No API key. Off by default. |
| **Kraken** | Crypto spot pairs | WebSocket stream | No API key. Off by default. |
| **Bybit** | Crypto spot pairs | WebSocket stream | No API key. Not served in some regions. Off by default. |
| **Simulated** | Anything | Local random walk | For demos, screenshots and offline development. Off by default. |

Sources that are off by default are enabled in **Settings → Data sources**. To show an instrument from one of them,
pick it in the *Add instrument* window or drag the sources into your order of preference on the *Data sources* tab: the first enabled source that supports an asset class is the default for it.

> Market data may be delayed or inaccurate and is **not investment advice**. FluxTick is not affiliated with any of
> the data providers. Respect each provider's terms of use.

## Getting started

### Download

Grab the latest build from the [Releases](https://github.com/coldhighsun/FluxTick/releases) page:

- **Windows:** `FluxTick-<version>-win-x64-setup.exe` installs for the current user (no administrator rights needed).
  Uninstalling keeps your settings in `%APPDATA%\FluxTick`.
- **Windows (portable):** `FluxTick-<version>-win-x64-portable.zip` contains a single `FluxTick.exe`; unzip it anywhere and run it, no installation needed.
- **Linux:** `FluxTick-<version>-linux-x64.zip` contains a single `FluxTick` executable.
- **macOS (Apple Silicon):** `FluxTick-<version>-osx-arm64.dmg` contains `FluxTick.app`; see [macOS installation](#macos-installation) below.

> **Do not mix the portable build and the installed version on the same machine.** Both share the same settings in
> `%APPDATA%\FluxTick`, and each registers its own *Launch at startup* entry pointing at its own `FluxTick.exe`, so the
> two will conflict (the wrong copy may start at sign-in, or the entry may be overwritten). The built-in updater also
> installs the setup program, so a portable copy that updates itself turns into an installed one. Pick one, and
> uninstall the installed version (or delete the portable folder) before switching.

The installer and the portable build are not code-signed, so Windows SmartScreen may warn on first run.

#### macOS installation

The macOS build is signed ad hoc only: it has no Apple Developer ID signature and is not notarized. macOS Gatekeeper
therefore blocks it on first launch ("cannot be opened because the developer cannot be verified" or "is damaged"). It
does not run by double-click right away; approve it once:

1. Open the `.dmg` and drag `FluxTick.app` to `Applications`.
2. Open FluxTick from `Applications`. macOS refuses and shows a warning.
3. Open *System Settings → Privacy & Security*, scroll to the message about FluxTick and click *Open Anyway*, then
   confirm. (On macOS 14 and older, right-click the app and choose *Open* instead; macOS 15 removed that shortcut.)

Alternatively, remove the quarantine flag in a terminal before the first launch:

```bash
xattr -dr com.apple.quarantine /Applications/FluxTick.app
```

Things to know on macOS:

- Only Apple Silicon (M1 and later) is built, there is no Intel build. macOS 12 or later is required.
- The boss-key hotkey and *Launch at startup* are Windows-only and not available. Clicking the menu bar icon opens the
  instrument list (experimental on macOS); its *Show / hide*, *Settings* and *Exit* entries sit at the bottom of the list, as
  the icon has no menu of its own.
- There is no Keychain integration yet: API keys are stored as plain text (file mode 600) in `~/.config/FluxTick`, which
  also holds the settings and logs.
- The app is a single self-extracting file, so the very first launch can take a few seconds longer.
- Updates are not installed automatically: FluxTick downloads and verifies the new `.dmg` and tells you; replace the app
  in `Applications` yourself.

On Windows, FluxTick looks for a newer release when it starts and about once a day while it runs (stable releases only, pre-releases are ignored). It then offers to download, verify and install the new setup program, and starts again when done. This can be turned off under Settings → Updates.

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
| Add an instrument | **+** in the header, or *Add instrument…* in the right-click menu of the empty area; pick an asset class and a data source, then search |
| Reorder | Drag a row up or down |
| Remove, change source | Right-click a row |
| Move the Settings / Add window | Drag its header; **Esc** closes it |
| Settings | The gear icon in the header, *Settings* in the tray menu, or at the bottom of the right-click menu of the empty area. Tabs: *General*, *Appearance*, *Groups*, *Tray* (Windows only), *Alerts*, *Data sources* and *Updates*. Expand a data source to edit its options; drag data sources to set their priority |
| Launch at startup | Windows only, off by default; Settings → General |
| Start in the tray | Off by default; Settings → General (or the `--minimized` option) keeps the window hidden at startup until you double-click the tray icon (a single click where the system cannot list the instruments, i.e. anywhere but Windows) |
| Single instance | Only one copy runs per user; starting FluxTick again brings the running window forward, even when it is hidden in the tray |
| Close button | Hides the window to the tray by default; turn off *Close button hides to tray* in Settings → General to exit instead |
| Tray menu | Right-click the tray icon for *Show / hide*, *Settings* and *Exit* |
| Tray list | Windows only; click the tray icon to see up to five instruments with price and change, without opening the window. Right-click an instrument and choose *Add to tray list* (or *Remove from tray list*); reorder under Settings → Tray. Stopped prices are dimmed and show why. Esc or a click elsewhere closes it; double-click the icon to open the window |
| Restore a data source | *Restore defaults* in its expanded card resets its options (secret values such as keys are kept) |
| Data source status | The footer shows one dot per data source in use (green connected, orange off or failing, grey idle or reconnecting), problems first, with a **+N** chip when not all fit. Click the footer for every source's state and error message |
| Full screen | Double-click the header to fill the screen; double-click again or press Esc to go back to the previous size and position. Dragging the header in full screen leaves it and carries on dragging. If you quit in full screen, the next start is full screen again |
| Compact mode | Hides the charts of the list layout (right-click menu of the empty area, or Settings) |
| Card layout | The grid button in the header switches between list and cards (also in the right-click menu and Settings → Appearance) |
| Card size | In card layout, pick *Small*, *Medium* or *Large* from the header, the right-click menu or Settings → Appearance. Small shows only symbol and price (the rest is in the tooltip); large adds a taller chart, the change in amount and percent, the reference price and the 60-minute low and high |
| Group cards | In card layout the header's grouping button (or the right-click menu, or Settings → Appearance) picks *No grouping*, *By type*, *By data source* or *Custom groups*. Click a group header to collapse or expand it; drag a type or custom group header to reorder |
| Custom groups | A *Default* group exists from the start, and new instruments join the first group. Create a group from the grouping button → *New group…*, the right-click menu, or a card's *Move to new group…*. Double-click a group header to rename it; right-click for *Rename…* and *Delete group* (only while it is empty and not the last group). Move cards by right-click → *Move to group* or by dragging. Settings → Groups reorders and deletes groups (deleting there moves the instruments to the first remaining group) |
| Price roll | Off by default; *Roll the price on change* in the right-click menu or Settings → Appearance. Only the price number rolls; the flashing card or row marks an alert that fired and does not depend on this setting |
| Edge auto-hide | On by default (needs always-on-top; unavailable where the system does not let a window leave the screen); Settings → General. Drop the window at the left, right or top screen edge and it slides away, leaving a thin strip; point at the strip or double-click the tray icon to bring it back. It hides again once you move away, unless it was brought out with the tray icon |
| Hide / show | While a tray icon is available the window has no taskbar button (and is not in Alt+Tab). **×** hides to the tray (or exits, see *Close button*); double-click the tray icon to bring it back (a single click where the tray cannot list instruments); *Exit* in the tray menu quits. Prices keep updating while hidden, so the sources with an instrument on your watchlist stay connected |
| Boss key | A global hotkey (default **Ctrl+Alt+H**) hides every window and the tray icon at once; the same key brings them back. Change it in Settings → General: click the box and press the new keys (at least one of Ctrl, Alt, Shift or Win plus a letter, digit, F key, arrow, navigation or punctuation key; Backspace turns it off). Prices keep updating while hidden, but alert notifications and update prompts wait. Windows only |

Settings and logs live in `%APPDATA%\FluxTick` on Windows and `~/.config/FluxTick` on Linux and macOS; Settings → General has buttons that open the folders. Logs are one file per day (split at 10 MB), and only the newest 5 files are kept. A problem that repeats with every price update is logged at most once a minute.
Options at their built-in default are not written to `settings.json`. Delete it to reset everything.

### API keys

Some data sources need an API key (today only Eulerpool; such sources are off by default until you enter the key under Settings → Data sources). Keys are entered in a masked field and handled like this:

- **Where they are kept:** in `settings.json`, in the same folder as the other settings. They are not kept anywhere else, never written to the logs and never put in a URL; they are only sent to the data source they belong to.
- **Windows:** keys are encrypted with the Windows Data Protection API (DPAPI) for your user account and written as `protected:v1:…`. Only the same user on the same machine can open them, and each one is bound to its own data source and option, so it cannot be moved to another. A key from an older version that was stored as plain text is encrypted the first time the app starts. Older copies of the settings file, such as `settings.json.bad`, may still hold it as plain text.
- **Linux and macOS:** there is no built-in protection yet, so keys are stored as plain text. The file is created readable and writable by your user only (mode `600`), so it is as private as your user account.
- **Limits:** DPAPI does not stop other programs running as your user from reading a key, and a key is visible in memory while the app runs. Do not share `settings.json` or paste it into an issue; remove the key first.
- **If a key cannot be opened** (the file was copied to another account or machine, or is damaged), the data source asks for the key again. The old text stays in the file while the source is on, so an unrelated change does not lose it; entering a new key replaces it, and switching the source off lets go of it.
- **Restore defaults** on a data source keeps its keys.

## Project layout

```
src/FluxTick.Abstractions        Stable provider contracts (the only thing a data source references)
src/FluxTick.Core                Provider registry, routing, quote aggregation, sparkline series, settings
src/FluxTick.Providers.Common    Helpers shared by data sources (WebSocket and polling base classes, symbol search)
src/FluxTick.Providers.Binance   Binance WebSocket source
src/FluxTick.Providers.Okx       OKX WebSocket source
src/FluxTick.Providers.Coinbase  Coinbase WebSocket source
src/FluxTick.Providers.Kraken    Kraken WebSocket source
src/FluxTick.Providers.Bybit     Bybit WebSocket source
src/FluxTick.Providers.Yahoo     Yahoo Finance WebSocket source
src/FluxTick.Providers.Tencent   Tencent Finance polling source
src/FluxTick.Providers.Sina      Sina Finance WebSocket source
src/FluxTick.Providers.Eulerpool  Eulerpool WebSocket source (API key)
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

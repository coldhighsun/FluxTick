# CLAUDE.md

## Quote providers

- Prefer a push transport (WebSocket or another streaming API) to fetch quotes. Fall back to polling only when the data source offers no streaming option.
- When a source offers both, use the stream for live prices and polling only for what the stream does not carry (for example history or a snapshot on connect), or as a fallback while the stream is down.

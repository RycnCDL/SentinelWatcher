# SentinelWatcher

A web dashboard that shows Microsoft Sentinel workspace health: analytics rules and their incident counts, log ingestion volume and estimated cost, data connector gaps, data collection rules, and Sentinel health events.

> Status: v0.1.0 is in preparation on the `release/v0.1.0` branch. Full installation and configuration documentation will be added before the release.

## Repository layout

| Path | Content |
|------|---------|
| `src/api` | ASP.NET Core Web API (C#) |
| `src/client` | React + Vite + TypeScript single-page app |
| `scripts` | Helper scripts, for example the Entra app registration setup |

## License

MIT, see [LICENSE](LICENSE).

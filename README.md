# TrueMain

TrueMain is a League of Legends analytics site. It answers one question per champion: **what do the players who truly main this champion actually do?** — and turns that into build, rune, skill-order and matchup guidance backed by real match data instead of raw pick-rate averages.

The site is live at [truemain.lol](https://truemain.lol).

## The idea

Most stat sites aggregate every game played on a champion, which drowns the signal in one-trick-free noise. TrueMain instead identifies *true mains* — players whose games-played-to-mastery ratio on a champion shows sustained, current investment — and computes its statistics from their matches only. The result is closer to "what do dedicated specialists build and how do they play the early game" than "what does the average queue do".

## How it works

Data flows through three .NET services and two Nuxt frontends:

```
Riot API ──► Ingestor ──► PostgreSQL ──► Api ──► web (truemain.lol)
                              │                └─► admin (ops portal)
                              └── MongoDB (logs, audit, Riot-usage metrics)
```

### Ingestor — the data pipeline

A .NET worker that runs a set of scheduled, independently recorded processes:

- **Discovery** walks ranked ladders across platforms to find candidate players.
- **Match ingestion / harvest** pulls solo/duo (queue 420) match and timeline data for known accounts. Other queues are dropped at ingest — TrueMain is ranked-only by design.
- **Scoring / main analysis** computes the games-vs-mastery signal that promotes a player to *true main* status for a champion.
- **Aggregation** folds match participants into per-champion aggregates: builds, runes, skill orders, matchups, timeline leads (gold/XP deltas), kill positions, jungle clears. Aggregates are computed per patch and rank scope; past patches are frozen once their live matches are retired.
- **Retention** trims raw match data that has already been aggregated, keeping the database bounded.

### Api

An ASP.NET Core service exposing the read side: champion aggregates, true-main leaderboards, player profiles, and authenticated `/ops` endpoints for the admin portal. Reads are purpose-built query objects in the `Data` project returning read models — the API layer stays persistence-ignorant.

### Data & Core

`Data` owns the EF Core model (PostgreSQL), migrations, repositories used by the ingestion write side, and the Mongo-backed logging/metrics sinks. `Core` holds shared League domain types (platforms, regional routing, queues, ranks).

### web

The public Nuxt frontend: champion pages (builds, runes, skill order, matchups, timeline leads), player pages, and search. It talks to the Api through a Nitro server proxy, so the browser never reaches the backend directly.

### admin

A standalone Nuxt portal (separate deployable, not a route of `web`) for operating the pipeline: process runs and health, manual seeding, Riot API usage metrics, and data audits. Sits behind session authentication.

## Repository layout

```
backend/   .NET solution — Api, Ingestor, Core, Data, tests (unit, integration, test kit)
web/       public Nuxt frontend
admin/     standalone Nuxt admin portal
docs/      API reference, RFCs, production migration notes
compose*.yaml  Docker stacks (dev with hot-reload, QA and prod from published images)
```

## Infrastructure

Everything runs as Docker containers on a single host. In production, Caddy terminates TLS (Let's Encrypt) and is the only public entry point; the frontends and databases live on an internal network. CI builds the backend in Release with analyzers as errors, runs unit and integration tests (against real PostgreSQL), builds both frontends, and publishes images to GHCR. Pull requests get an automated Claude code review with a blocking verdict.

## Further reading

- [`docs/api.md`](docs/api.md) — HTTP endpoint reference (parameters and response shapes).
- [`docs/diagrams/architecture.drawio`](docs/diagrams/architecture.drawio) — architecture diagram.
- `docs/phase-*.md` — RFCs behind the major evolutions of the data model and pipeline.

# Refactor — Option C backlog

> **Status: partially implemented — C-2, C-6 and C-13 are done, C-9 is half-done,
> the other nine are still open. This is a living backlog, not a historical record:
> the open items below are still the proposal to implement. Last verified
> against the code on 2026-08-26.**

This document captures the items the comprehensive .NET / Ingestor / API / Infra
audit surfaced as **valuable but out of scope** for the Option-B PR
([refactor/dotnet-cleanup](../README.md)). They share one of three traits:

- they require **breaking** internal contracts (constructor signatures,
  config keys, public service interfaces),
- they require a **new external dependency** (a NuGet package, a vault
  service, an observability backend),
- they need a **data migration** that warrants its own review.

Each item is tagged with the audit zone (Core / Data / Api / Ingestor / Infra
/ Tests), the rationale, the proposed approach, and the visible blast radius.

---

## C-1. Replace the `RecordedProcess` decorator's manual exception rethrow with structured logging context

- Status: **open** — `RecordedProcess` still ends its catch block on a bare
  `throw;`, and no `IngestorProcessFailedException` exists.
- Zone: Ingestor
- Why: today `RecordedProcess` catches, logs, and rethrows. The rethrown
  exception loses the async stack and downstream handlers (Worker, the new
  outer try/catch) cannot tell whether the failure already produced a
  ProcessRun audit row or not.
- Approach: add a `ProcessRunId` to the wrapping exception (custom
  `IngestorProcessFailedException`) so observers can correlate the audit
  row with the rethrown failure.
- Risk: any catch block elsewhere keying off `Exception` type would have to
  be updated.

## ~~C-2. Drop the legacy `ChampionPatternAggregate` table~~ — **DONE**

Closed by Phase 6 (RFC: [`docs/phase-6-pattern-junction-rfc.md`](./phase-6-pattern-junction-rfc.md), PRs 6.1 → 6.4). The legacy `champion_pattern_aggregates` table is gone, along with the per-scope `champion_aggregate_*` dim tables and the dual-write code path. The aggregator now writes the new pattern junction (`champion_aggregate_patterns` + globally-deduplicated `champion_dim_*`) only, and the read side projects from there. (The `?buildId=` correlation pivot this item originally shipped with has since been replaced by the tabbed builds endpoint, #81.)

## C-3. Backfill the SkillEvents truncation against existing data

- Status: **open** — the ingestion-time truncation is in place, but no migration
  ever rewrote the existing `match_participants.skill_events` rows.
- Zone: Data + Ingestor
- Why: this PR truncates new ingestions to 11 events
  (`TimelineIngestionService.MaxSkillEventsPerParticipant`). Rows already
  in production keep their longer arrays — the storage saving only
  materialises after every match has been re-ingested or rewritten.
- Approach: a one-shot SQL migration that rewrites
  `match_participants.skill_events` to keep the first 11 by `TimestampMs`.
  Should be batched by `created_at_utc` to keep the lock window small.
- Risk: irreversible; needs a snapshot before running.

## C-4. Externalise compose secrets

- Status: **open** — none of the compose files declares a `secrets:` block; every
  credential still travels through `.env` into container env.
- Zone: Infra
- Why: `compose.yaml`, `compose.dev.yaml`, `compose.preprod.yaml` and
  `compose.prod.yaml` interpolate `POSTGRES_PASSWORD`, `RIOT_API_KEY`,
  etc. from `.env`. The variables end up in container env, visible to
  anything that can `docker inspect`.
- Approach: switch to Docker secrets (`secrets:` with `external: true`),
  store passwords in the host's secret manager (1Password / Bitwarden /
  Doppler), and update `Api/Program.cs` + `Ingestor/Program.cs` to read
  the connection string from the mounted file when present.
- Risk: every environment (local dev, preprod, prod) needs the secret bag
  provisioned before the next deploy.

## C-5. Move the API key for `/ops/*` to a token store

- Status: **open** — no `IOpsKeyValidator`; `OpsOptions.ApiKey` is still a
  `[MinLength(32)]` string and `ApiKeyAuthenticationHandler` still takes
  `IOptionsMonitor<OpsOptions>`.
- Zone: Api
- Why: `OpsOptions.ApiKey` is a single static string with `[MinLength(32)]`
  validation. Rotating it requires a redeploy.
- Approach: introduce a `IOpsKeyValidator` abstraction; the file-system
  implementation reads from a mounted secret, the future implementation
  pulls from a token store. The handler stays the same.
- Risk: `ApiKeyAuthenticationHandler` constructor signature changes (it
  takes the validator instead of `IOptionsMonitor<OpsOptions>`).

## ~~C-6. Adopt `Microsoft.Extensions.Http.Resilience` (Polly v8) for the Riot HTTP clients~~ — **DONE**

- Status: **done** — `Microsoft.Extensions.Http.Resilience` is pinned in
  `Directory.Packages.props`, `Ingestor/Riot/RiotResilienceExtensions.cs` wires
  `AddStandardResilienceHandler()` onto every typed Riot client from
  `Ingestor/Program.cs`, and `RiotHttpExecutor` was deleted (zero occurrences).
  The Community Dragon client uses the same handler.
- Zone: Ingestor
- Why: `RiotHttpExecutor` now handles 429, 5xx and network failures with a
  hand-rolled exponential backoff. Polly's `AddStandardResilienceHandler`
  provides the same shape, plus circuit breaker, timeout policy and
  metrics, with one DI line per typed client.
- Approach: add the package, register the handler in `Ingestor/Program.cs`,
  drop the manual loop in `RiotHttpExecutor.GetAsync`, keep
  `GetRetryDelay` for the `Retry-After` header (Polly does not honour it
  natively for arbitrary delegates).
- Risk: behavioural change in retry timing; acceptance criteria need a
  full match-ingestion run in preprod before promoting.

## C-7. Idempotent match ingestion via `ON CONFLICT`

- Status: **open** — `MatchSnapshotWriter.PersistMatchAsync` still persists
  through EF change tracking (`Add` / `AddRange` then one `SaveChangesAsync`).
  Raw `ON CONFLICT` exists elsewhere (aggregation, dedup) but not on this path.
- Zone: Data + Ingestor
- Why: `MatchSnapshotWriter.PersistMatchAsync` adds participants and
  perks one-by-one; a transient failure mid-batch leaves partial rows
  that the next run reinserts. The current "claim then revert on failure"
  flow is correct but relies on the lease, not on the row state.
- Approach: rewrite the persistence path to use
  `ExecuteSqlRawAsync("INSERT ... ON CONFLICT DO NOTHING")` for `Match`,
  `MatchParticipant`, and `ParticipantPerkSelection`. Idempotency keys
  already exist (the natural keys are unique).
- Risk: bypasses EF change tracking; tests must cover concurrent inserts.

## C-8. Batch timeline fetches behind a `SemaphoreSlim` throttle

- Status: **open** — `TimelineIngestionService.IngestTimelinesAsync` is still a
  serial `foreach`; there is no `SemaphoreSlim` and no `Task.WhenAll` anywhere in
  the Ingestor.
- Zone: Ingestor
- Why: `TimelineIngestionService.IngestTimelinesAsync` fetches timelines
  serially (`foreach`). With ~100 matches/account and Riot's per-second
  cap (20/s production), one run takes minutes that could be parallelised.
- Approach: a `SemaphoreSlim(20)` (matching the per-second budget) inside
  a `Task.WhenAll` block. The same shape works for `MatchSnapshotWriter`'s
  match-detail fetches.
- Risk: needs careful coordination with the new resilience handler from
  C-6 — both must respect the same budget or we'll thrash the limiter.

## C-9. Replace `IDataRepositoryFactory` + `IDataSessionFactory` with direct DI — **HALF DONE**

- Status: **half done** — `IDataRepositoryFactory` / `DataRepositoryFactory` are
  gone (zero occurrences), so step 1 and step 3's first half landed. Step 2 did
  not: `IDataSession`, `DataSession`, `IDataSessionFactory` and `DataSessionFactory`
  still live in `Data/Repositories/`, with 26 files under `Ingestor` still
  consuming them (45 repo-wide, counting Api, Data and tests). Remaining work is
  swapping those consumers onto the DbContext plus a transactional
  `SaveChangesAsync` extension.
- Zone: Data
- Why: today the chain is
  `IDataSessionFactory` → `DataSessionFactory` → `IDataRepositoryFactory`
  → repository instances. Four files for one job. The container can
  produce all of these directly via standard DI.
- Approach:
  1. Register each `IXxxRepository` as scoped against the DbContext.
  2. Replace `IDataSession` with the DbContext + a transactional
     `SaveChangesAsync` extension.
  3. Remove the two factory pairs.
- Risk: every consumer of `IDataSession` (Ingestor processes mostly)
  takes a constructor signature change.

## C-10. OpenTelemetry for traces + metrics

- Status: **open** — no OpenTelemetry package, no OTLP exporter, no collector in
  any compose file.
- Zone: Api + Ingestor + Infra
- Why: today there is no distributed tracing, no metrics export. The
  Audit's "production will be blind" call-out for observability holds.
- Approach: add `OpenTelemetry.Extensions.Hosting`, instrument EF Core and
  HttpClient out of the box, push traces to OTLP. Pair with a Grafana
  / Prometheus stack in the compose file (or commercial alternative).
- Risk: deployment story needs the new collector / backend.

## C-11. Parallelise the CI matrix

- Status: **open** — `ci.yml` still runs unit then integration tests sequentially
  inside a single `backend` job. A NuGet cache exists, but within that one job,
  so the cross-job caching part of the approach is moot rather than done.
- Zone: Infra
- Why: `ci.yml` runs unit tests then integration tests sequentially. With
  a 36-file integration suite using Testcontainers, this dominates wall
  time on PRs.
- Approach: split into two GitHub Actions jobs (`test-unit`,
  `test-integration`) running in parallel. Cache the NuGet `~/.nuget`
  directory across jobs. Optionally shard integration tests by class
  with `dotnet test --filter`.
- Risk: a flaky integration test now blocks merge in parallel — but it
  blocks anyway.

## C-12. Rate-limit the discovery + match-ingestion processes against Riot

- Status: **open** — no `IRiotRateLimiter`. C-6's `AddStandardResilienceHandler`
  brings a per-client concurrency limiter, but not the `(RegionalRoute, Method)`
  token bucket shared across clients that this item asks for.
- Zone: Ingestor
- Why: today `DiscoveryProcess` and `MatchIngestionProcess` hit the
  Riot platform routes back-to-back. Combined with C-6's resilience, a
  centralised per-route token bucket would prevent self-imposed 429s.
- Approach: a `IRiotRateLimiter` keyed by `(RegionalRoute, Method)`, used
  by every Riot client.
- Risk: requires careful tuning to avoid starving low-priority calls
  (timeline fetches) when high-priority calls (match ingestion) flood.

## ~~C-13. Drop the `Initialized` flag in `PlatformId`~~ — **DONE (variant)**

- Status: **done, but not by the approach below.** The `Initialized` boolean is
  gone: `PlatformId` now stores a single `_routePlusOne` sentinel field, and
  `PlatformRoute` was renumbered so `BR1 = 1`, leaving 0 unused and
  `default(PlatformRoute)` invalid. No `Unknown = 0` member was added, so the
  persisted-values migration this item feared was still needed — it happened as
  part of the renumbering, not as a deprecation cycle. Known leftover: the XML
  doc and inline comments in `Core/Lol/Identifiers/PlatformId.cs` still describe
  `BR1` as the enum's zero value, which is no longer true.
- Zone: Core
- Why: `PlatformId` uses an `Initialized` boolean to detect
  `default(PlatformId)` because `BR1` is the zero value of
  `PlatformRoute`. The pattern works but adds a field per instance and
  defensive checks on every accessor.
- Approach: add `PlatformRoute.Unknown = 0` to the enum, treat it as the
  invalid sentinel. `default(PlatformId)` then has `Route = Unknown` and
  the throwing accessors collapse to a single check.
- Risk: changes the persisted enum values for **all** existing rows.
  Needs a data migration + a deprecation cycle.

---

## Out-of-scope items deliberately rejected

- **Wrap `RiotRouting`, `RiotDataHelpers`, `ItemTransformations`,
  `ItemMetadataProvider`, etc. in interfaces for DI.** These are pure
  functions / immutable data. Adding an interface adds indirection
  without testability gain — the audit's recommendation was dogmatic.
- **Add `ILogger<T>` to every service.** Logging is valuable at
  ingress / egress points (controllers, HTTP clients, processes), not at
  every projector / mapper / builder. Inflating the dependency surface
  hurts more than it helps.
- **Add validation DTOs to controller query parameters when clamping
  in the service is enough.** `ChampionsController` already clamps
  `maxDepth` and `minBranchGames` server-side; introducing
  `[Range]`-decorated DTO classes adds 80 lines per endpoint for the
  same effect.

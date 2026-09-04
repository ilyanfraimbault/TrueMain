# Performance, caching and incidents

Part of the [decision log](../decisions.md). Format: **Decision** — why — `source`.

**An expensive read path behind a TTL cache needs a single-flight, not a lock** (#870,
`Api/Services/RequestCoalescer.cs`). The cache protects steady state; the stampede happens on the first
request and at every TTL expiry, when concurrent callers all miss at once and each start the same scan — up to
50 000 scored accounts for one dedication ranking. Concurrent callers now share the in-flight `Task`. A per-key
lock was rejected: it serialises callers *and* still runs the work N times whenever the result is too large to
cache, turning a burst into a queue of full scans. The shared pass is detached from any one request's token —
a caller who walks away must not cancel the pass the others are waiting on — so only bounded work belongs in it.

**Postgres runs with `max_parallel_workers_per_gather=0` in every compose file — do not re-enable.**
2026-06-25 prod crash-loop: `53100: could not resize shared memory segment` — per-worker DSM segments
exhausting the container's 256 MB `/dev/shm` in bursts. EF read it as transient and retried, which under
`restart: unless-stopped` became an API + ingestor crash loop. Disabling beat raising `shm_size` because it is
deterministic (raising only moves the ceiling) and the API workload is index/PK reads — #589.

**Consequence: every heavy aggregate runs single-threaded, so batch work must be chunked.**
Three separate 300 s command-timeout incidents followed — pattern aggregation's `DISTINCT ChampionId` scan
(#603), timeline leads (#594), and harvest candidate aggregation (#632, dead since 2026-07-03). The last one
is instructive: *no code changed*, it was a slow threshold crossing.

**Aggregation is chunked per champion to bound memory.**
The ingestor was OOM-killed at ~6 GB managed heap because the source reader materialised every live-patch
participant *with* its item/skill event JSON and the builder held a second copy. An OOM bypasses the
per-process try/catch, and `restart: unless-stopped` replayed the same step — a crash loop that took the whole
VPS down — #600.

**A heavy `CREATE INDEX CONCURRENTLY` must never be a startup migration.**
The #595 covering index exceeded the migrator's `Command Timeout=300` (CONCURRENTLY also waits for the
ingestor's in-flight transactions to drain) → `TimeoutException` → API crash-loop retried on every restart →
deploy aborted on an unhealthy container, leaving an INVALID index that needed its own cleanup migration —
#597 / PR #598.
**Refinement, not a reversal:** once `match_participants` shrank to ~2M rows / ~11 GB after #680, the index was
rebuilt as a *plain transactional* migration — the timeout risk no longer held at that size, and
`CONCURRENTLY` plus a manual pre-create step meant prod's schema was not reproducible from migrations alone.
Accepted: a `SHARE` lock stalls the ingestor for the tens of seconds of the build — PR #750.
👉 The rule is about **heavy** index builds, not about partial single-column indexes on a table of this size.
**Generalised (#1227): no migration may contain a statement Postgres refuses inside a transaction — at all.**
`migrationBuilder.Sql(..., suppressTransaction: true)` does not buy an escape hatch on the real deploy path.
That flag only has an effect for `Database.MigrateAsync()`, and `ApplyMigrationsOnStartup` is permanently
`false` in preprod and prod. Deploys run `dotnet ef migrations script --idempotent` piped into
`psql --single-transaction`, which defeats it twice over: `--idempotent` wraps *every* statement in a
`DO $EF$ ... END $EF$` PL/pgSQL block (Postgres: `CREATE INDEX CONCURRENTLY cannot be executed from a
function`), and `psql` opens an explicit transaction around the whole file (so a procedural `COMMIT;` inside a
`DO` block is an `invalid transaction termination`). Six migrations carried one or the other. It was silent
because preprod and prod already had them in `__EFMigrationsHistory` and the idempotent guards skipped their
blocks — it would only have surfaced on the first database built from scratch: a new preprod, a DR restore,
onboarding. All six were rewritten as plain transactional DDL, which is free: their bodies now only ever
re-execute on an empty database. The `migrate-fresh` CI job applies the generated script to a blank Postgres
on every PR so this cannot come back.

**Npgsql pools are capped per service (api 50, ingestor 20) against Postgres `max_connections=100`.**
Two unbounded pools defaulting to 100 each could request 200 connections; once truemain.lol went live this
produced `53300: sorry, too many clients already` and a total API outage. PgBouncer was proposed as the proper
fix; the caps are what actually shipped (no `pgbouncer` service exists in the compose files) — #437, #461, #462.

## Postgres ships tuned settings in compose, and parallelism stays off (2026-09-02)

**The server no longer runs on compiled defaults.** Until now the only setting either compose file overrode
was `max_parallel_workers_per_gather=0`; everything else was stock `postgresql.conf`, sized for a 1 GB
machine, on a host where Postgres is the dominant tenant (prod: 4 vCPU, 16 GB RAM, NVMe, a 38 GB database,
8.8 GB RSS and ~13 GB of the host in page cache, measured 2026-09-02). `compose.prod.yaml` now passes a
pgtune-style "mixed" set of `-c` flags and `compose.preprod.yaml` the same *settings* at roughly half the
*values*, because preprod is a smaller host sharing its box with the whole preprod stack — same plans
exercised first, smaller footprint (#1366, part A).

**Why the risky ones.** `shared_buffers=4GB` is the conventional 25 % ceiling past which double buffering
against the OS page cache costs more than it returns. `effective_cache_size=11GB` is a planner hint only, and
it matches the cache actually observed — at the old 4 GB the planner under-priced index scans it should have
preferred. `work_mem=32MB` with `hash_mem_multiplier=2` is per sort/hash *node*, so the real ceiling is
several multiples of it per connection; it is affordable here only because pgbouncer caps the pool, and it is
what stops the 2.7 M-row `match_participants` folds from spilling to disk at 4 MB. `random_page_cost=1.1` and
`effective_io_concurrency=200` say the volume is flash, not a spinning disk. `jit=off` because JIT compilation
is pure overhead on queries this short. The autovacuum scale factors drop to 0.05/0.02 because the largest
tables bloat far faster than the stock 0.2 reacts.

**`max_parallel_workers_per_gather=0` stays, permanently.** It is not a leftover to clean up while tuning the
neighbouring settings: it is the fix for the /dev/shm exhaustion incident (#589), where parallel workers on
the aggregate-pattern queries exhausted the shared-memory segment, Postgres raised 53100, and the API and
ingestor crash-looped. Both compose files carry a comment saying so. `shm_size` went 256m → 1g as insurance
now that the server is allowed real memory, but that does not make parallelism safe to re-enable.

**`pg_stat_statements` is preloaded by compose and created by a migration, and the migration is tolerant.**
`shared_preload_libraries` is a start-up setting, so `CREATE EXTENSION` fails on any server that does not
carry it — a developer's local Postgres, the integration suite's throwaway container, a restored dump. The
migration wraps the statement in a `DO` block that catches the failure and raises a `NOTICE`, so a database
without the preload does not break the migration chain. The consequence, and it is the general rule for any
migration that depends on a setting shipped with it: the `migrate-*` job runs *before* the deploy restarts
Postgres, so the first run always takes the NOTICE branch and is stamped in `__EFMigrationsHistory` anyway —
nothing retries it. Each environment gets one manual `CREATE EXTENSION IF NOT EXISTS pg_stat_statements;`
after the restart, documented in `docs/production-migrations.md`.

**Both changes only take effect on a Postgres restart.** `shared_buffers` and `shared_preload_libraries` are
start-up settings. Preprod restarts on the next deploy from `develop`; prod moves only on a published
release.

## Champion reads are cached until the data changes, not for 60 seconds (2026-09-02)

The champion reads were each caching themselves, with their own `TryGetValue`/`Store` pair and a 60 s TTL —
and five of them (`ChampionBuildsQueryService`, the live matchup fold, `GetTrioSynergiesAsync`,
`ChampionTrendQueryService`, and the since-removed `ChampionPatchDiffQueryService`) were not caching at all. `RequestCoalescer`
existed but only the truemains leaderboard used it. With 173 champions × 5 lanes × rank brackets, a 60 s TTL
means practically every visit to a non-top champion pays the cold price, and nothing stopped ten concurrent
visitors from each paying it at once.

**One entry point, because the two halves only work together.** `IChampionReadCache.GetOrComputeAsync` caches
*and* single-flights *and* sizes the entry. A cache without single-flight turns each expiry into a stampede of
identical multi-second scans; single-flight without a cache re-runs the pass for the next visitor; and an
entry without a `Size` is silently dropped by the size-limited shared cache (see `ApiCache`). Leaving those
three as three things a new service must remember is how you get a read that looks cached and is not.
`ChampionReadCacheRegistrationTests` walks `ChampionsController`'s constructor — the DI surface of the
champion reads — and fails if any service behind it takes a raw `IMemoryCache` or no cache at all.

**Keyed by aggregation version, not by a clock.** The reads served from the aggregate tables only change when
the ingestor rewrites them, once per cycle and never in between, so a 60 s TTL was throwing away answers that
were still exactly right. The key carries a token derived from `MAX("AggregatedAtUtc")` over
`champion_aggregate_scopes`; a new cycle changes the token and retires every entry at once, with nothing to
enumerate or evict. The token read is itself cached for 5 s and single-flighted, so it costs at most one
`max()` every five seconds no matter how much traffic arrives — the one thing that must not happen is the
version probe becoming the new hot query. An empty database is just another version (`none`), so a first-ever
fold invalidates the empty answers by moving the token.

**For the live folds the backstop *is* the freshness bound, and that is the trade.** Roam, scaling, item
timings, powerspikes, synergies, the live branch of matchups, mains-comparison and the composition selection
read `match_participants` directly, so they also move with match ingestion — which since #1374 runs in a lane
of its own, decoupled from aggregation. The token does not track that, so those answers can sit up to the
30-minute absolute expiry where they used to sit one minute. Accepted knowingly: these are precisely the reads
that measured 2–5 s cold, the staleness is bounded and never a wrong answer, and part B of #1368 moves them
onto aggregate tables of their own, after which the token covers them too. The absolute expiry also remains
the guard against a token that somehow stops moving.

**The owner of a coalesced pass waits it out.** `RequestCoalescer` grew an `ownerAwaitsToCompletion` flag for
this. The champion reads run on the caller's request-scoped `DbContext`, so if the caller that *started* the
pass abandoned its wait, its scope would be disposed underneath the shared work and every joiner would fail on
a disposed context. The leaderboard does not need the flag — it creates its own context — and it does not get
it.

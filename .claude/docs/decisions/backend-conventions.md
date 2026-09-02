# Backend code conventions

Part of the [decision log](../decisions.md). Format: **Decision** — why — `source`.

## Where API reads live

**Decided 2026-07-30 (#865): the documentation was stale, and now matches the code.** API reads are
purpose-built **query services** returning read-models, living in `Api/Services/<area>` beside the endpoints
they serve, injecting `TrueMainDbContext` and projecting with `AsNoTracking`.

The rule had said reads live in `Data` with `Api` staying persistence-ignorant. Measured on 2026-07-30, 83
`*QueryService*.cs` files sat under `backend/Api/Services`, 36 of them injecting the context, while
`backend/Data` held only Mongo-side query objects (`MongoLogQuery`, `CrashQuery`, `RiotApiUsageQuery`). The
contradiction was re-litigated on six separate PRs, each time resolved by following the code and leaving the
doc alone — which is the right short-term reflex and the wrong durable answer.

What was kept, because it held everywhere and is the half that matters: **no generic `IRepository<T>` for
reads.** Every read path is shaped by the question it answers, returning a read-model rather than entities.
The rejected alternative was migrating 36 services (plus tests and DI) into `Data` for an architectural
benefit no user would ever see.

`Data` still owns: the schema (entities, configurations, migrations, the compiled model), the Mongo-side query
objects, and **SQL that must not diverge between two consumers** — e.g.
`Data/DataQuality/ChampionDimensionCanonicalKeys.cs`, read by both the ingestor's rune-page repair and the
admin duplicate detector (#924). That last one is the durable reason `Data` holds any query text at all: a
detector that groups differently from the repair it audits reports a clean bill of health for a live bug.

Counts drift with every PR — re-measure rather than trusting the numbers above:

```bash
find backend/Api/Services -name "*QueryService*.cs" | wc -l
grep -l "TrueMainDbContext" $(find backend/Api/Services -name "*QueryService*.cs") | wc -l
```

## Tables are snake_case, columns are PascalCase — and enums split by who reads them (2026-08-28)

**Written down because a review read the table names and inferred the wrong rule** (#1251). "Postgres schema,
therefore snake_case everywhere" is a reasonable guess and it is wrong here: every table is snake_case
(`champion_matchup_stats`, `riot_accounts`), and every column is quoted **PascalCase** (`"ChampionId"`,
`"IsMain"`, `"PowerspikeAggregated"` — see the raw SQL filters in `MatchConfiguration` and
`Data/DataQuality/ChampionDimensionCanonicalKeys.cs`). There is exactly one exception, `elo_bracket`, mapped by
hand with `HasColumnName` in seven configurations. Applied literally, the "snake_case columns" reading would
have produced new snake_case columns in the middle of a PascalCase schema — making the mix worse in the name
of fixing it.

**Nothing is renamed.** Aligning either side is a heavy migration over the largest frozen tables in the
database and buys nothing a reader cannot get from one sentence. What is guarded is the drift:
`SchemaNamingConventionTests` fails when a new table is not snake_case, or a new column is neither PascalCase
nor the allow-listed `elo_bracket`. Do not extend that allow-list — an entry there is the inconsistency
spreading, which is the only outcome this decision exists to prevent.

**Enum persistence follows who reads the column, and that was previously unwritten.** Three shapes coexisted,
each justified locally and none globally: `SeedRequestConfiguration` stores text (`HasConversion<string>`,
"readable in ad-hoc SQL"), `MainCandidateConfiguration` and `RiotAccountConfiguration` store ints — which is
why the partial index on `riot_accounts` has to spell `"MatchIngestStatus" <> 0` in raw SQL, with a comment
apologising for it — and `ProcessRunDocument` uses `BsonType.String` on the Mongo side. The rule, for **new**
enums:

- **A lifecycle state an operator reads or writes by hand goes to text.** Seed request status, anything an
  admin panel exposes, anything that turns up in an incident's psql session. The width is irrelevant next to
  a query that says what it means.
- **An internal flow flag stays an int.** Claim/lease states, fold progress markers — columns only code
  touches, sitting in hot partial indexes, whose set of values changes with the code that reads them.
- **Mongo documents always store enums as strings** (`BsonType.String`). Those collections are read ad hoc by
  definition, and a Mongo document has no migration to rescue a renumbering.

No retroactive migration: the existing three stay as they are. This settles which one a new column copies.

**Postgres columns stay quoted PascalCase; only tables are snake_case.**
#227 proposed `UseSnakeCaseNamingConvention` for columns and was **closed as won't-do on 2026-07-28**: a
full column rename is a large, risky migration for a cosmetic gain, and raw SQL, prod psql habits and the
compiled model all rely on the current naming.

**The EF compiled model must be regenerated on every schema change.**
`dotnet ef dbcontext optimize` → `Data/CompiledModels`. Originally for cold start; the operational reason is
that a stale model *silently drops columns*. Two concurrent schema PRs always conflict there — the second
merged must re-merge develop and regenerate — #242, `CLAUDE.md`.

## Configuration defaults live in the class, and the two champion games floors are two keys

**`appsettings.json` only carries what differs from the class default.** The ingestor's file used to restate
~30 keys that were already the default of their `*Options` class, which made `/configuration` (#1034) useless:
every one of them was tagged *override* though nothing was overridden, and that noise hid the single key that
genuinely diverged — `Discovery:MaxAccountsPerPlatformPerRun`, 500 in JSON against a class default of 350, so
for months nobody could say which value was in force. It was 500. That value moved onto `DiscoveryOptions`
(both deployed stacks override it anyway: 750 prod, 100 preprod) and the JSON key is gone.
`IngestorAppSettingsNoDefaultsTests` now fails the build if any key comes back equal to its class default; the
documentation-only empty sections (`Riot`, `CommunityDragon`, `Job`) and the list-valued keys stay, because a
list default deliberately lives in JSON — the binder *appends* to a non-empty list instead of replacing it
(#860).

**`ChampionsList:MinSampleGames` (10) and `ChampionsList:MinBuildSampleGames` (20) are different questions.**
The first decides whether a `(champion, lane)` line is listed and ranked at all; the second decides whether an
item/rune distribution *inside* a line is a usable sample — it splits its games across several builds, so it
needs more of them. The build floor used to be two hard-coded `20`s, in `ChampionBuildsQueryService` and
`PlayerBuildDivergenceQueryService`, each documented as a mirror of the other with no code link between them,
while an operator reading `MinSampleGames = 10` on `/configuration` or `/patch-coverage` would infer the wrong
bar for the build panel. Both now read the new key, and the whole `ChampionsList` section is on the
configuration page. The existing key was **not** renamed: a config-facing section rename breaks deployment
(#889).

**When a process needs candidate writes, it goes through `IDataSession`.** `MatchDataRetentionProcess` used to
`new` a `MainCandidateRepository` over its own `DbContext` — the only hand-built repository in the ingestor —
although the purge it wanted is already on `IDataSession.MainCandidates`. `IDbContextFactory` stays the right
tool for the set-based deletion passes in the same file, which are raw `ExecuteDelete` work over a scoped
context; a repository operation is reached through the session.

## A unit of work covers the writes and nothing else (2026-08-28)

**Transactions wrap writes, never Riot calls.** `MatchIngestionProcess` used to open its per-account
transaction *before* up to 40 Riot round-trips (20 match-v5 + 20 timelines), each able to burn the client's
whole `EffectiveTotalRequestTimeout` under a 429 backoff. The connection sat `idle in transaction` for minutes
per account, holding the claim locks and pinning VACUUM's horizon — the exact counter-model of #264, which had
already removed that pattern from MainAnalysis. Ingestion now runs in two phases: a fetch phase that
materialises the DTOs (bounded by `MatchIngestion:MatchesPerAccount`, so a few MB per account), then a
transaction around the writes only. The property the transaction was opened for is unchanged: a crash still
cannot leave a partially ingested match, and a crash during the fetch phase writes nothing at all — the replay
is idempotent through `GetExistingMatchIdsAsync` and the `TimelineIngested` flag (#1229).

**A trailing `SaveChangesAsync` after `ExecuteUpdate` calls is not a commit point.** `ExecuteUpdate` /
`ExecuteDelete` never enter the change tracker, so the save commits nothing and only makes the code *look*
atomic. `AccountValidationService` chained three of them bare: a failure between the first and the last left
candidates `Validated` while the account stayed `Processing` for a whole claim lease. All three of its exit
paths — `ValidateAsync`, `RevertAsync` and `ReleaseUningestableAsync` — now run inside an explicit
transaction, and the decorative saves are gone. The release path is the one where a partial failure hurts
most: the account would keep its place at the head of the claim ordering and be re-claimed at once, only to
prove uningestable again (#1229).

**`ChangeTracker.Clear()` is safe only when the batch owns everything it loaded.** The ingestor's long loops
(Scoring, Discovery, AccountRefresh, participant harvest) drain the tracker after each batch save via
`IDataSession.ClearTracking()` — without it every `SaveChanges` re-runs `DetectChanges` over every entity the
run has ever touched, which is quadratic in the number of batches. The catch: three of those loops preloaded
tracked entities for the *whole* run and mutated them later (rank snapshots overwritten in place, harvested
candidates re-scored). Clearing under a run-wide preload detaches them, and a detached entity accepts property
writes and persists none — silent data loss, not an error. So each preload moved inside its batch. Don't
"optimise" one back out to the top of the loop: the three loops that carry the risk (Discovery,
AccountRefresh, the participant harvest) each have an integration test that runs more than one slice and
fails if the preload is hoisted — a unit test cannot catch this, since a mocked `IDataSession` makes
`ClearTracking()` a no-op (#1229).

**The Ingestor's file heartbeat is liveness, not progress.** It used to be touched once per loop iteration, so
it went stale for a whole `Job:IntervalMinutes` (60 min) plus a whole `Full` pass; the healthcheck had to
tolerate 6 h of silence, which left it unable to detect anything short of a process dead for a quarter of a
day. A dedicated 30 s loop now refreshes it for the worker's whole lifetime and the threshold is 300 s, so a
wedged process is caught in minutes. Whether the *work* is progressing is a separate question with a separate
answer already in place: the `process_runs` heartbeat, which ages a stalled run out to `Abandoned` (#1229).

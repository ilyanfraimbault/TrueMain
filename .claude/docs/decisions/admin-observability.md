# Admin portal — observability data

Part of the [decision log](../decisions.md). Format: **Decision** — why — `source`.

## Admin-portal data lives in Mongo, not Postgres (2026-08-01)

**Decision:** everything stored *for the admin portal* belongs in the Mongo observability database
(`truemain_logs`), not in the SQL schema. Postgres keeps the TrueMain product data — matches, accounts,
candidates, aggregates — i.e. what ingestion produces and the public site reads. This finished what #416
(logs), #93 (Riot usage), crashes and #925 (storage snapshots) started: the last two admin-only tables,
`process_runs` and `seed_requests`, moved to Mongo collections of the same names.

Consequences that matter later:

- `process_runs` finally has retention (180 d TTL index on `startedAtUtc`) — the SQL table had none and grew
  forever. `seed_requests` has **no** TTL: it is a functional operator queue, not telemetry, and its store
  **throws** when Mongo is unconfigured instead of silently dropping an operator's request (the
  observability stores no-op by design).
- The ManualSeed claim (`Pending→Resolving`) is a single guarded Mongo update — same no-TOCTOU semantics the
  SQL `ExecuteUpdate` gave. The Riot-ID idempotency check uses a strength-2 collation (case-insensitive,
  accent-sensitive) instead of escaped ILIKE.
- Run summaries are stored as **raw JSON text** (`summaryJson`), not nested BSON, so what the admin renders
  is byte-identical to what the recorder serialized — no BSON→JSON number reshaping.
- Documents store Guids and enum names as plain strings, deliberately: shell-readable, and the one-shot
  SQL→Mongo backfill stays a dumb `row_to_json` transform.
- The SQL tables were **not dropped in the same PR**: the code switch shipped first (no schema change, no
  compiled-model churn), the historical rows were copied preprod/prod once the frozen tables had no writers,
  and a follow-up PR drops the tables + regenerates the compiled model.

**The disk figure sums Postgres and Mongo, because there is one disk** (#1023). The panel and the forecast
measured Postgres only, while Mongo holds the logs, crashes, audit events, Riot rollups, process runs and seed
requests — two of those (`audit_events`, `seed_requests`) with **no TTL at all**, i.e. the collections most
likely to surprise us were exactly the ones nothing watched. Verified on prod before building: every named
volume uses the default local driver and lands on a single `/dev/sda1` (193 GB), so the saturation date was
optimistic by construction. The daily rollup therefore takes one reading per engine and **sums** them; taking
the max — which is what grouping by day alone did — would have reported whichever engine is larger as the
disk. The snapshot grain gains an `engine` discriminator, and the upsert key becomes `(day, engine, name)`:
`process_runs` and `seed_requests` exist as both a (frozen) Postgres table and a Mongo collection, so without
it one engine's reading would overwrite the other's every day. Pre-#1023 documents are stamped `postgres`
on the next index pass — they are Postgres readings by construction, and leaving the field absent would make
the engine-filtered upsert insert a *second* document for the same day and table, silently doubling it.

**A step change in what is measured is not growth, so the forecast restarts at it** (#1023). The day Mongo
first gets measured adds its whole footprint at once. Fitted whole, the series reads that one-off jump as a
daily rate and predicts a saturation that is not coming. The forecast is therefore fitted only over the
trailing days that measure the *same set of engines* as the most recent one — so it goes absent for three
days after the change, which is a state the panel already explains, rather than confidently wrong for ninety.
Splicing the series or backfilling a Mongo number for days nobody measured were both rejected: the honest
answer to "we changed the instrument" is a gap, not a reconstruction.

**Ingestion throughput is measured from the run summaries, not from `matches.CreatedAtUtc`** (#1025). The
overview's existing chart buckets `GameStartTimeUtc` — when the games were *played* — which is a property of
the player population: it barely moves when ingestion stalls, and a backfill makes it grow in the past. The
portal therefore carried no signal for "did the pipeline keep up", which is exactly what the two retention
crash-loops (#982, #988) needed. `matches.CreatedAtUtc` exists and is the obvious source, but retention deletes
out-of-window and non-tracked-queue matches, so an old bucket shrinks over time and the curve rewrites its own
history. The `MatchIngestion` run summaries in Mongo are retention-proof by construction: deleting a match does
not rewrite the run that ingested it. The cost is a 180-day ceiling (the `process_runs` TTL), which the
response reports so the panel states the bound rather than drawing the tail beyond it as zero ingestion.
The two charts stay side by side and separately labelled — they answer different questions and merging them
would lose one.

**The counters are summed in memory because the summary is stored as opaque JSON text** (#1025).
`ProcessRunDocument.SummaryJson` is a string on purpose (#990: the admin receives byte-identical bytes to what
the recorder wrote), so Mongo cannot `$sum` a field inside it. The split is the one that plays to each side:
the server does the indexed `(processName, startedAtUtc)` range scan and projects two fields, the read parses
and does the arithmetic. Three consequences are deliberate. A run with **no** summary — failed, abandoned, or a
no-work pass whose shape differs — still counts as an attempt with absent counters, because dropping it would
make a crash-looping ingestor read as an idle one. Quiet periods **inside** the observed range are zero-filled,
since a stalled pipeline is the thing the chart exists to show. And nothing is filled **before** the oldest
surviving run: that period was not measured, and zeros there would assert a repose we have no record of.

**The candidate funnel measures Validated and Demoted, because Rejected is a status nothing assigns** (#1024).
The issue asked for a rejection counter, on the reading that rejections were the funnel's missing outcome. They
are not missing — they do not exist: `MainCandidateStatus.Rejected` is read in five places (the pruning
predicate, the harvest's refusal to resurrect, the manual seed's requeue list, the admin filter, the overview
breakdown) and **assigned in none**, so the `Rejected` bucket the portal has always shown is structurally zero.
Adding the requested counter would have shipped a permanently flat series dressed as a measurement. What the
funnel genuinely lacked was its *exit*: `AccountValidationService` promotes Processing → Validated without
feeding any summary counter, so nothing recorded how many accounts cleared ingestion. That counter
(`MatchIngestionSummary.AccountsValidated`) is what got added, alongside `MainAnalysisSummary.DemotedAccounts`,
which already existed and is the pipeline's only real negative outcome. Whether a rejection verdict should
exist at all — or whether the status should be removed, since several guards branch on a value that cannot
occur — is a product question, left to #1029 rather than smuggled into a chart.

**A forward-only counter renders as absent, not as zero, and key presence is what says which** (#1024).
`accountsValidated` did not exist before this deploy, so every `MatchIngestion` run already in the 180-day
window lacks the key. Summing absent-as-zero would have drawn months of "the pipeline validated nobody", which
is the exact failure #924 named. The series therefore reports the *first run whose summary carried the key* and
nulls every bucket before it. The boundary is global rather than per-bucket on purpose: past it, a period whose
only runs were no-work passes really did validate nothing, and a null there would hide a genuine stall behind
the same signal used for "not instrumented yet".

**`ValidatedAtUtc` had never been written in production, and the queue-latency snapshot is why that surfaced**
(#1024). The column, its migration and its API field had shipped long ago, but every Validated transition went
through `SetStatusForAccountAsync`, whose `ExecuteUpdateAsync` sets `Status` and nothing else — so the column
was null on every row, and the admin's candidate detail had been rendering an em dash for it since it existed.
The promotion now goes through a dedicated `MarkValidatedForAccountAsync` that stamps both in one statement,
rather than a flag on the generic setter: this is the only transition that owns that column. One behavioural
consequence is deliberate and was previously dead code — `PruneStaleNeverPromotedAsync` filters on
`ValidatedAtUtc == null`, so a candidate that was validated and later demoted is now genuinely exempt from the
stale prune, which is what "never promoted" always claimed to mean.

**Queue latency is a snapshot over retained rows and is labelled as one, rather than being faked into a series**
(#1024). `main_candidates` carries the three timestamps, so percentiles per past week look computable — but
retention prunes stale candidates, so any historical bucket would be computed over a survivor set that keeps
shrinking. The snapshot is the honest version of what those columns can answer, and it ships with its own bias
stated (pruning skews the survivors towards candidates that moved) and its sample count next to each percentile.
It takes no window parameter, which is the API making the same point structurally: there is no period to select.

**The candidate stock is snapshotted hourly, because it cannot be reconstructed afterwards.**
The funnel (#1024) measures flow from the run summaries and the status list measures the stock, but only right
now. The level over time is neither: a period that promotes everything it scores leaves the level flat, and so
does a pipeline that has stopped. Deriving it from `main_candidates` after the fact is impossible in principle,
not merely unimplemented — there is no `QueuedAtUtc`, so Scored and Queued are indistinguishable in the past,
and pruning and the demotion drain delete rows, so every past level would be understated by whatever has since
been removed. Hourly rather than daily because two of the six statuses are transient by construction: Scoring
drains the whole `New` backlog each run and `Processing` is a claim held for one ingestion pass, so a daily
reading shows both at 0 forever and can say nothing about scoring falling behind or leases not being reaped
(#1344). The platform stays in the key though the panel sums it away — the per-region split is the subject of
#1149/#1150, and a stock summed at write time could never be broken back down — #1403.

**A recorded zero is a measurement; an unmeasured period is absent.**
The candidate-stock snapshot writes every status for every observed platform, zeros included, because `New: 0`
is the healthy state (scoring drained its backlog) and has to stay distinguishable from an hour nobody
measured. On the read side the inverse holds: periods with no snapshot are left out of the payload entirely,
and the chart expands them back onto the period grid with undefined values so an outage stays a gap in the
curve. Joining across it would draw the one shape that reads as "nothing happened" over what was a stall —
#1403, #924.

**A stock is sampled across time and summed across platforms — never the other way round.**
A period holding several hourly readings reports its last one; adding two readings of the same 419,000 queued
candidates would report 838,000 of them. Within one reading the per-platform counts *are* summed, because those
are disjoint populations at a single instant. The two reductions are not interchangeable, which is why the
query service does them in that order and the tests pin it — #1403.

**Daily storage snapshots go to Mongo and are keyed on the day, not the run.**
Storage history is append-only, time-ordered, ops-only telemetry with no relational joins — the exact
criteria that put logs and metrics in Mongo below — and a native TTL index prunes it for free instead of
needing its own retention arm in `MatchDataRetentionProcess`. Keying the document on `(day, table)` rather
than on the capture instant is what makes the writer safe to run every pipeline pass: prod runs the ingestor
`RunOnce` + `restart: unless-stopped`, so it re-runs back-to-back many times a day, and a day-keyed upsert
turns that into "refresh today's reading" instead of "append another point". No scheduler, no "have I run
today" guard, and a container restart can neither lose nor duplicate a day — #925.

**The disk forecast is absent rather than approximate when the data can't support it.**
It declines to project on fewer than 3 days of history, on flat or shrinking storage, on a crossing more than
a century out, and when no disk capacity is configured; the panel prints which of those applies. A forecast is
the one number on the admin that is invented rather than measured, and an operator who cannot tell a fitted
date from a placeholder will either ignore all of them or act on a fabricated one. Related: the projected
figure is measured `pg_database_size`, not the sum of `pg_total_relation_size` over public tables — the sum
excludes catalogs and would under-report what actually fills the volume that #680 filled — #925.

**Logs and metrics live in MongoDB, not Postgres, with two different guarantees.**
Mongo has native TTL retention and suits append-heavy time-ordered payloads. `logs` is lossy (bounded channel,
batched, drop-on-overflow — fine for diagnostics); `audit_events` is lossless and synchronous. Existing
Postgres log rows were deliberately not migrated — #416.

**Ops logs are signal-only: Polly retry noise is not persisted, domain events are.**
Every Riot 429 emitted `Execution attempt` + `OnRetry` warning pairs — dozens per minute while rate-limited —
burying everything useful — #444. This does not apply to `riot_api_call_rollups`: that collection exists
specifically to count every physical attempt including retried 429s (#93), a different data path from the
structured logs #444 is about.

**Riot API caller attribution uses an `AsyncLocal` ambient context, mirroring `IterationContext`.**
`riot_api_call_rollups` needed a "who spent this budget" dimension (#1035) but the three typed Riot clients
are shared `HttpClient`s across every `IIngestorProcess`, so there is no per-caller `HttpClient` to key on.
`Worker.RunModeAsync` opens a call scope (`ICallerContext.BeginCall(process.Name)`) around each process's
`RunCoreAsync`, the same shape `IterationContext` already uses for the pass-level iteration id; the metrics
handler reads it ambiently. The rollup's upsert key widened from `(bucket, endpoint, statusCode)` to include
`callerProcess`, so the unique index had to be dropped and recreated (`MongoLogContext.EnsureRiotApiCallIndexesAsync`)
rather than just adding a field — two different callers in the same minute/endpoint/status are two documents,
which the old 3-field unique index would have rejected as a duplicate key.

**The budget-headroom estimate (#1035) requires 24h of rollup history before it will extrapolate, and picks
the app rate-limit window with the smallest daily ceiling as "binding".**
Riot returns several simultaneous app-limit windows (e.g. `20:1,100:120`); the one that binds first under
sustained load is the smallest `limit * 86400 / windowSeconds`, not the one with the highest current-instant
usage ratio (that's what the existing app-rate-limit card already shows). Below 24h of observed history the
page renders an explicit "not enough data" state instead of extrapolating a daily cost from a thin window —
same reasoning as the disk forecast's absent state below. Implementation:
`RiotApiUsageQueryService.BuildHeadroom`/`ResolveBindingLimit` (`internal static`, unit-tested directly).

## The configuration viewer is an allow-list, and each host reports itself (2026-08-08)

**Decision:** `/ops/configuration` (#1034) exposes sections through a hand-declared allow-list
(`EffectiveConfigurationCatalog`) that names an options type and, when needed, an explicit
`IncludeProperties` subset — never a full options dump filtered for secrets afterward. A deny-list is one
forgotten property away from returning the Riot key, connection string or `X-Ops-Key`, and the property
that leaks is always the one added after the filter was written. A name-shaped backstop
(`EffectiveConfigurationRedaction.IsSecretName`) drops anything credential-shaped that slips into a
catalog entry anyway, and a unit test walks both production catalogs asserting that backstop never has to
fire — so a new section with a secret-shaped property fails a test, not a review.

**The Api and the Ingestor report themselves through two different mechanisms, on purpose.** The Api reads
its own `IOptions<T>` live, on every request — it can introspect its own container, so there is nothing to
cache. The Ingestor cannot be introspected by the Api (different container, and its options classes live
in an assembly the Api does not reference), so it publishes a snapshot of its bound options to Mongo
(`effective_configuration`, one document per process, **overwritten** at every boot — it runs `RunOnce` +
`restart: unless-stopped`, many boots a day, and the page wants "what is it running now", not a log of
every past boot) via a boot-time `IHostedService`, registered ahead of the pipeline's own worker since a
fast `RunOnce` pass can end the process before a later-registered service would run. Consequence: the
Ingestor's "as of" timestamp can be older than the page load without meaning anything is stale, while the
Api's is always "now" — the page states this per process rather than presenting both timestamps the same
way.

**"Servable" is a share of the settled patches' median, never an absolute line count.** The patch-coverage
verdict (#1033) asks whether enough `(champion, lane)` lines clear `ChampionsList:MinSampleGames` for the
directory and tier list to mean anything — and the honest bar for that moves with the corpus. The number of
lines clearing ten games grows every time tracked accounts are added, so a hard-coded "300 lines" would read
permanently green on production and permanently red on preprod, which is the same as having no check. The bar
is therefore `PatchCoverage:ServableLinesRatio` (0.6) of the **median of the patches strictly older than the
one being served** — the settled ones. The served patch and anything newer are excluded from their own
reference on purpose: a still-filling patch dragged into the median pulls the bar down to whatever it
currently is, and the check goes green on an empty patch. That is the same "the edge patch is not comparable"
rule the patch-volume detector already applies (#924). `PatchCoverage:ServableLinesMinimum` is the fallback
when a database holds a single patch (preprod's normal state) — crude, and still an answer rather than a
shrug.

**A fold that shipped mid-corpus reports `null`, never `0`, on the patches it predates.** Raw match payloads
are not kept, so #920's bans and #957's per-opponent spikes can never be backfilled: their absence on an older
patch is a property of when the fold shipped, not a failure. Printing `0 rows` there sends an operator hunting
a bug that does not exist, so the read model carries `measured: false` with the first patch the fold ever
wrote a row on, and the page prints "not measured before *patch*" **in place of** the counts rather than
beside them. The first-measured patch falls out of the same grouped scan that produces the per-patch numbers,
so distinguishing the two costs nothing — `PatchCoverageQueryService`, #1033.

## The pipeline chain is drawn per lane, not as one flat list (2026-09-02)

**The chain view groups an iteration's runs by the lane that ran them, and shows each lane at its own newest
iteration.** The panel was built when a pass ran all 20 steps: it drew `PIPELINE_CHAIN` in full and annotated
whatever the iteration did not contain as `notRun`. Since #1362 an iteration belongs to *one* lane, so on a
two-lane deployment every pass painted the other lane's dozen steps as grey "Not run" chips — a claim that is
simply false, and one that pushed the two lanes into looking like one broken pass. Grouping says the true
thing instead, and the tree it produces (chain → lane → steps) is also the shape the operator reasons in: the
lanes are what run concurrently.

Three details. **A `Full` iteration lights up both branches**, so prod's current topology renders under the
same code path rather than a special case — the branches are the pipeline's shape, not the deployment's.
**The top block asks for several iterations, not one**: the lanes have different cadences (preprod runs about
three fetch passes per aggregate pass), so the slower lane's newest iteration sits a few positions down a
newest-first list, and showing "the newest iteration" would hide whichever lane started earlier. **A branch's
duration comes from its runs' statuses, not their timestamps** — the API mirrors a running run's start into
`finishedAtUtc` so the iteration's last-activity stamp advances, so measuring from the timestamps alone
reports a lane that has been running for minutes as `0ms`.

`PIPELINE_LANES` is a hand-maintained copy of `JobModeSequence`'s two lanes, exactly as `PIPELINE_CHAIN` is
one of `FullPipeline`, and pinned by the same kind of test — a partition of the chain, in the chain's order.
The drift is worse than the flat chain's was: a step in neither lane is not misplaced, it stops being drawn
at all. A run whose process no lane declares therefore still renders, in a trailing branch under its raw
name — #1399, #1362, #1314.

## An admin number is either actionable or it is not printed (2026-09-03)

**The below-floor list on `/patch-coverage` names a champion's primary lane only, and `/champions` has no
`Ext. samples` column.** Both were true numbers that answered nothing, and they failed in the same way: a
figure that is 0 (or noise) on every row trains an operator to skip the panel it lives in.

The below-floor list exists to give a thin patch a **cause** — "16.17 is four lines short, here is which
ones". Measured on production, of the 146 below-floor lines on 16.16 and the 194 on 16.17, **not one** was on
the champion's most-played lane of that patch: they are one-game Kai'Sa TOPs and Riven UTILITYs, short of
games because nobody plays the champion there, not because the patch is short of matches. Naming them buried
the lines that were genuinely two games off under a wall of picks no amount of ingestion will ever move. The
primary lane is read off the patch's own lines (the lane holding the most of the champion's games there),
never from a hard-coded role: the patch a champion is being flexed on is exactly the case a fixed role gets
wrong. It lives in `ChampionDirectoryLines` with the definition of a line itself, not in the query service —
the same reason the line count does.

`belowFloorCount` now counts what the list is drawn from rather than every line under the floor, and the tail
is **derived** rather than carried in a second field: every below-floor line is `lines - linesPastFloor`, both
of which the card already prints, so the off-role remainder is a subtraction the page states in its own
sentence. Dropping the difference silently was the one option ruled out — a number that quietly stops adding
up is how a page loses trust. When every below-floor line is off-role, that sentence *is* the section: an
empty list under "146 lines below the floor" reads as a bug, and "no champion is short of games on its own
lane" is the answer.

Two follow-ups from reading it in preprod. The list is drawn on **the current patch only**: it is a to-do,
and the only patch anything can still be done about is the one the site serves. An abandoned patch has every
champion a game or two short, so its card printed 51 names carrying one game each — and a reader who had
scrolled past the card's heading took them for the served patch's, which is how a champion sitting on 738
games of the live patch appeared to be nine short of the floor. And each line prints `3 / 10 games` rather
than `3 · 7 short`: a shortfall against a bar the row does not state is not a number anyone can read.

`Ext. samples` counted the mains that only cleared the *relaxed* per-champion play-rate threshold (#407) —
a diagnostic of the coverage relaxation, not a property of the champion. On production that is 111 rows out
of 68 056 mains, at most 5 for any one champion and exactly 0 for 123 of the 173, so as a sortable column it
was a column of zeros with no label saying what it measured. It now rides under the **Mains** figure it
qualifies, as "N relaxed", rendered only when non-zero and explained in the panel's info popover. The
`/ops/stats/champions` payload is unchanged: the field was never the problem — #1442, #1033, #407.

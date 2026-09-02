# Ingestion pipeline — Riot budget, pacing and intake sizing

Part of the [decision log](../decisions.md). Format: **Decision** — why — `source`.

**Riot calls are paced by a limiter keyed on the routing value, because that is the grain Riot enforces.**
The standard resilience handler's "rate limiter" strategy is a *concurrency bulkhead*, not a request-rate
limiter, so until #1359 nothing bounded the outbound rate: pacing was reactive, discovered by taking a 429 and
honouring `Retry-After`. Production was taking ~1 000-1 300 of them a day. The limiter (`Ingestor/Riot/RateLimiting/`)
keeps one budget per Riot routing value — `europe`/`americas`/`asia` for the regional APIs, `euw1`/`kr`/`na1`
for the platform ones — which is what Riot actually meters; a single global budget would have throttled the
process to one region's allowance while the others idled.
Four consequences worth stating, because each is a place where the obvious implementation is wrong.
**The window is sliding where Riot's is fixed.** Riot resets on a wall-clock boundary we cannot observe, so a
fixed window of our own would straddle theirs and take a 429 on the seam; a sliding window forbids a superset
of what theirs does, at the cost of a little unused headroom right after a reset.
**Riot's advertised header replaces our limits, it does not merge with them.** The header states the complete
budget, so merging would leave the configured guess (a personal key's `20:1,100:120`) in force behind a
production key that allows far more — and the tighter of the two always wins, which would silently cap the
throughput the new key was obtained for. Windows whose duration survives keep their counters, so adopting a
limit never forgets what has already been spent, and a header that parses to nothing changes nothing.
**A 429 with no `X-Rate-Limit-Type` penalises the whole routing value, not the endpoint.** Riot omits the type
when the throttle came from the underlying service; attributing that narrowly would keep hammering whatever is
actually exhausted.
**Acquisition is serialized per routing value.** The budget is a property of the routing value, so "may I send
now" has to be decided one caller at a time to stay correct. It costs nothing: the sustained personal-key
allowance is under one request per second per region, and regions never wait on each other — which is the
whole point.
**The two halves of the limiter sit on opposite sides of the resilience handler.** The first attempt put both
inside it, reasoning that a retried attempt should wait for its own permit. Preprod disproved that within an
hour of the deploy: inside the resilience pipeline, the wait for a permit is charged to the **10-second
per-attempt timeout**, and a legitimate wait on a 100-per-2-minutes window is routinely longer than that. The
attempt was cancelled on the queue rather than on the network, retried into the same queue, and the account's
whole ingestion failed with a `TimeoutRejectedException` raised from the limiter's own `Task.Delay`.
So **waiting happens outside** the resilience handler, bounded by `HttpClient.Timeout` (sized above the total
request budget), and **observing happens inside** it, where it still sees every physical attempt — including
the 429s the retry strategy absorbs, which are exactly the responses carrying the `Retry-After` the next
acquisition must honour. The cost of the split is that the limiter charges one permit per *logical* request
rather than per attempt; Riot's own `X-App-Rate-Limit-Count` on the way back is what corrects the count, which
is what that header is for. The metrics handler stays innermost, so a permit wait is never recorded as Riot
latency, and the Riot clients' `HttpClient.Timeout` is sized to cover the wait *and* the pipeline beneath it —
sized for the pipeline alone, a long-but-legitimate wait followed by a slow pipeline would trip it and surface
as the same opaque `TaskCanceledException` #855 fixed one layer in. A single wait is itself bounded by
`RiotRateLimit:MaxPermitWaitSeconds`: past that ceiling the call is sent anyway, because a wait that long means
our model and Riot's have diverged, and a 429's count headers resynchronise the window where an unbounded wait
would just stall the pipeline behind one call. No configuration is needed when the key changes: the limits are
learned from the response.

**The pipeline runs as two lanes — Riot-bound and Postgres-bound — because they have opposite bottlenecks.**
The 20 steps were one serial chain, so the Riot key idled through every aggregation and the aggregates waited
behind ~55 minutes of HTTP; on a Discovery day they were 2.5 hours older than they needed to be. `FetchLane`
and `AggregateLane` are composite `JobMode`s, exactly like `Full`, so the split reuses the machinery that
already existed rather than adding a scheduler.
What makes it safe is that the sequence was never what ordered the work *between* steps: every aggregation
selects only the matches whose prerequisites hold (`TimelineIngested`, the per-fold flags on `matches`), so a
fold that runs early finds nothing and picks the rows up next pass. Order *within* a lane is still
load-bearing — the ban fold must see stamped elo brackets, the timeline prune must not precede the powerspike
fold — which is why the two lanes preserve the full pipeline's relative order, asserted by a test that also
pins them as a true partition of it: a step in neither would silently stop running, a step in both would fold
the same rows twice.
The one thing the split genuinely broke is orphan-run reconciliation. It abandoned *every* `Running` document
at boot, on the stated assumption of a single-instance ingestor; with two lanes that means each lane marks the
other's live run as dead on every restart. It is now scoped to the steps the booting lane actually owns, and
resolves those names with `GetKeyedService` rather than the required variant — an unregistered step is a
wiring mistake the run itself reports precisely, and throwing at boot would bury that behind a vaguer error.
An empty owned-set skips reconciliation rather than sweeping everything, because "I don't know what I own" is
the one case where a full sweep is certainly wrong.

**The lane a run belongs to is derived from its name; the mode it ran under is recorded.** Those are two
different questions and the panel needs both. Which lane a step belongs to is static, so the admin derives it
from the process name (`laneForProcess`) and draws one branch per lane — an iteration then renders its own
lane instead of painting the other lane's twelve steps as phantom "Not run" chips. What the name cannot
answer is what the pass *intended* to run: a deliberate `MatchDataRetentionOnly` and an aggregate lane that
has only reached its first step contain exactly the same thing. So `IIterationContext` carries the mode, the
recorder stamps it on every `process_runs` document, and the panel narrows a single-process pass to the step
that ran rather than surrounding it with skipped chips — #1362.

**Preprod runs the two lanes; prod stays on `Full` until preprod has.** Nothing about the code forces a
topology — a single container on `Full` still runs everything in order — so the split is a deployment
decision, and it is the kind that is cheap to validate on preprod and expensive to get wrong on prod (#1362).
**The claim orders by games played since the last visit, not by how long ago that visit was.**
Ordering by `LastMatchIngestAtUtc` alone spent the batch on whoever had waited longest, whether or not they
had played. On production that meant a **27-day median revisit** against a 20-game fetch window, so any main
playing more than ~0.7 games a day was losing games between visits — and the players who play most are exactly
the signal the site is built on. Meanwhile a third of every batch went to accounts that had played nothing.
The fix costs no Riot calls, because the answer was already being paid for: `LadderSync` reads ~94 k ladder
entries per run for ~310 calls and already refreshes wins/losses for every tracked Emerald+ account.
`riot_accounts.LadderGames` denormalises that sum, `LadderGamesAtLastIngest` records what it was when the
account was last ingested, and the difference is what the claim sorts on.
Three details that are not arbitrary. **The count is refreshed on the `Unchanged` snapshot path too** — a win
and a loss return to the same LP, and those are precisely the games that would otherwise go unnoticed. **The
baseline is reset inside the same statement that stamps `LastMatchIngestAtUtc`**, or the account would read as
freshly visited while still owing every game it owed before, and come straight back at the head of the next
batch. **An account with no ladder reading keeps the old age ordering** rather than sinking behind everything
that has one: below the swept tiers there is no signal, and a zero owed must not be read as "up to date".
**The same difference sizes the match-ids request**, so a fixed window stops truncating whoever plays most:
`count` widens to owed + a small drift margin, capped at Riot's 100, with `MatchesPerAccount` as the floor.
It costs nothing — the ids endpoint is one call whatever the count. The rule lives in `LadderGamesOwed.From`
because two callers apply it; the claim necessarily restates it as an expression tree, which the call site
says out loud.
**A missing baseline yields zero, not the whole season.** An account ingested before the columns existed has
no value to subtract, and reading the absence as zero would report a career's games as owed — after the
deploy that is every tracked account at once, which would order the pool by career volume rather than by
recent activity until each had been visited once.
**The difference is floored at zero**, because a Riot season reset restarts wins/losses from the bottom: the
raw subtraction then goes negative for every account at once and would sort the whole active pool behind the
accounts that owe nothing. It would self-heal on the next ingest — but "self-heal" there means a full sweep of
the pool, which is the very thing this ordering exists to avoid needing.
The count is denormalised onto the account rather than joined from `rank_snapshots` because it exists to sit
in an `ORDER BY` over the claimable set — a lateral join to each account's newest snapshot is the one query
the claim cannot afford to run per candidate row (#1360).

**Match ingestion fans out one worker per platform, and stays sequential inside one.**
The same #1359 measurement: a claim batch walked in one serial loop ran at 0.77 req/s — one region's sustained
allowance — while the other two regional budgets went unused. The fan-out is across routing values because
that is the grain of the budget; going wider *within* a platform would only queue behind the limiter that now
governs it, while adding contention on the same claim rows. Every collaborator it fans out to is stateless or
opens its own `DbContext` per account, so the only shared mutable state is the per-worker tally, merged in
platform order once the workers finish — the run summary is read by humans comparing one cycle to the next,
so a non-deterministic merge order would be a regression in its own right. A platform's failure stays its
own: the per-account catch was already there, and the result being local is what stops one bad account from
costing the other regions.

## A Riot call that stores nothing is a bug, not a cost (2026-09-02)

Over three days production spent ~77 k successful Riot calls a day, and roughly half the
`MatchIngestion` half of them produced nothing storable. The rule this PR settles: a call whose response
cannot become a row is not "budget spent on a low-yield path", it is a defect to fix, and the run summaries
must be able to show it (#1358, epic #1357).

**Flex ids were fetched, discarded, and re-fetched for ever.** The ids call sent `type=ranked`, which includes
queue 440, and `MatchSnapshotWriter` filtered on queue 420 *after* paying for the `GET /matches/{id}`. Nothing
is written for a discarded match, so `ExistingMatchScanner` saw the same id as new on every later claim of
that account — the waste recurred indefinitely rather than costing one call. The fix is `queue` on the ids
call, ANDed with `type=ranked` by Riot, sourced from `MainAnalysis:QueueId` rather than a literal so the
source filter cannot drift from the post-fetch guard, which stays as a safety net.

**The window is bounded by the last ingest.** `startTime` = `LastMatchIngestAtUtc` − 1 h (unset on a first
ingestion; the hour covers a game that started before the previous claim and ended after it). Without it every
claim re-listed the same fixed window, which is what made the flex re-fetch recur. `count` is clamped to
Riot's 1..100, so `MatchIngestion:MatchesPerAccount` stays the single authoritative knob and can be raised to
100 without a very active main being truncated at 20.

**Freshness gates, not new call paths, are how the crawls stop paying twice.** Discovery's per-entry
summoner-v4 call only supplies `profileIconId` / `summonerLevel` / `summonerId`, and every apex ladder entry
has carried its PUUID since #1312 — so for an account stored and synced within `Discovery:ProfileSyncFreshness`
(7 d) the call is skipped and the entry's own PUUID and rank are used. The same window skips the
champion-mastery call for a candidate whose rows were written inside it. `AccountRefresh:ProfileSyncFreshness`
(7 d) is the mirror for account-v1: reaching the head of the refresh queue is not on its own a reason to spend
a call. Both mirror the shape `AccountRefresh:RankSyncFreshness` already had.

**Two invariants make a skip safe.** A skipped row still gets its `UpdatedAtUtc` stamped, because every bucket
of `GetAccountsForRefreshAsync` drains oldest-first and an unstamped skip parks the row at the head of the
queue for ever (the #1223 failure mode). And a skipped call never writes the stamp it would have refreshed:
`LastProfileSyncAtUtc` is only set by a call that actually happened, otherwise the gate closes permanently on
a read that never occurred. AccountRefresh's gate additionally never applies to an identity-incomplete row —
account-v1 is the only writer of `GameName`/`TagLine`.

**The evidence lives in the run summaries, not in a new metrics pipeline.** `matchesSkipped` now means
"already stored" only, with the discards split out as `matchesSkippedWrongQueue`; Discovery reports
`profileCallsSkipped` / `masteryCallsSkipped` and AccountRefresh `profileSkippedFresh`. All are appended after
the existing keys, so a run recorded before the deploy reads as "not measured" rather than as a run that
skipped nothing. Deliberately **not** done here: recording discarded match ids in a table — with `queue` on
the ids call there is nothing left to record — and the ManualSeed pacing change, which interacts with the
candidate-funnel backlog (#1361) and belongs with it.

## The intake is sized by the claim, not by the ladder (2026-09-02)

**Every stage before the match-ingest claim used to carry its own absolute budget, and none of them was
derived from the one number that decides throughput.** Measured in production on 2026-09-02: Discovery
produced ~3 500 candidate rows a day, Harvest 7 500 per run (its budget exhausted *every* run, ~7 450 of them
refreshes), ManualSeed queued ~6 800, and Scoring promoted up to 900 — against a claim of
`MatchIngestion:BatchSize` 75 with `EstablishedMainShare` 0.7, i.e. **~22 new accounts per cycle**. The
result was `main_candidates` at 930 k rows, **773 k of them `Queued`**, 116 k dead tuples on a 441 MB table,
and a queue whose head never moved. The surplus was not buffer: a promoted candidate's score is recomputed,
rewritten and re-ranked every cycle, and at that ratio it goes stale years before the claim reaches it.

**So the claim is the sizing authority, and everything upstream is derived from it** (`IntakeCapacity`).
Capacity is `BatchSize - ceil(BatchSize x EstablishedMainShare)` — deliberately the complement of the claim
query's own expression, so the two cannot drift by a rounding step. Scoring's per-platform promotion is
capped at `capacity x Intake:PromotionHeadroomFactor / platforms`, Harvest's *refresh* budget at
`capacity x PromotionHeadroomFactor`, and retention demotes any platform holding more than
`Intake:MaxQueuedPerPlatform` back down.

**Three deliberate non-choices.** The existing knobs were not rewired: `Scoring:TopNPerPlatform` stays the
explicit ceiling and the derived cap only ever lowers it, so an operator can still clamp a stage by hand
without reasoning about the derivation. Harvest's *discovery* half — pairs with no candidate yet — keeps its
full configured share: finding unseen players is cheap, has no claim dependency, and is the half that stops
the pool converging on the region we already ingest most (#495). And the queue drain **demotes, never
deletes** — `Queued` → `Scored`, in bounded batches — because the row is the only record that the player was
ever seen, and a demoted candidate re-enters the ranking on the next cycle (#900's "deactivate, never
delete", one stage up).

**The claim's own split became adaptive too.** `EstablishedMainShare` is now the midpoint of a range, not a
constant: the quota-weighted coverage deficit the platform allocator already computes (#1150) swings it by up
to `Intake:EstablishedMainShareSwing` — towards new candidates when coverage is far below
`Coverage:TargetMainsPerChampion`, towards established mains when it is met. A *neutral* coverage snapshot
returns the configured value unchanged: its zero deficits mean "no signal", and reading them as full coverage
would tilt a cold-start claim towards established mains that do not exist yet.

**What this does not fix.** The pool is still apex-only by construction — Discovery reads Master/GM/
Challenger, so sub-Master accounts enter only by harvest luck. Discovering below Master on purpose needs the
ladder-delta sweep (#1360) and is not part of this. Neither is the per-`(platform, champion)` promotion cap:
capping per champion instead of per platform is the right shape — 40x over-supply on Ezreal should not crowd
out the five Amumu candidates that exist — but it needs a schema-backed index to be efficient, and this
change deliberately touched no schema.

## Region balance is a target, not a quota: coverage deficit allocates every budget (2026-08-19)

Match ingestion had settled at roughly **82% EUW1 / 14% NA1 / 4% KR** — the same split in stored matches,
tracked accounts, active mains and Riot calls. Nothing configured that split; it was the fixed point of a loop
with nothing opposing it. Every champion cleared `Coverage:TargetMainsPerChampion` on EUW1 while ~79% of
champions sat below it on KR, so champion pages presented a near-single-region sample as a global stat.

Three mechanisms compounded, and all three were "one global ordering over a pool that the previous run had
just fed":

- **Ladder discovery — the only per-platform-budgeted source — had been dead since 2026-06-14** (#1149): its
  cadence guard recorded each skip as a completed run, so the skip re-armed itself forever. That left the
  participant harvest as the sole feeder of new accounts.
- **The harvest can only reproduce the mix that produced it.** Its eligible pool is orphan
  `match_participants`, i.e. the matches we already ingested, so the densest observations are always the
  region we ingest most; ordering the union by observed games handed it the budget. Note `droppedNew` was
  **0** — the budget was never the bottleneck, the eligible pool was, which is why raising
  `Harvest:MaxCandidatesPerRun` would have changed nothing.
- **The claim ordered cross-platform by `LastMatchIngestAtUtc`, nulls first.** Right priority inside a region,
  fatal across regions: the region creating the most new accounts automatically captured most of the batch.

**The fix is not a configured per-region match quota.** "300 EUW, 300 KR" would be a guess, would need
re-tuning whenever a region is added, and would keep spending on a region that no longer needs it. Instead the
share follows the signal the pipeline already computes for champions:

- **Coverage is now keyed on (platform, champion)**, not champion alone (`GetMainCountsByPlatformAndChampionAsync`,
  `IX_main_champion_stats_is_main_champion` re-keyed to `(PlatformId, ChampionId)`). A champion-only count is
  dominated by whichever region we ingest most, so the one signal that could have damped the imbalance was
  blind to it: 60 EUW1 mains and 1 KR main read as *covered*, and every under-served region got a zero
  scoring bonus and no threshold relaxation.
- **`PlatformBudgetAllocator` splits every budget** — the claim batch and the harvest budget — by
  `weight(p) = 1 + MeanDeficit(p)`, apportioned largest-remainder. `MeanDeficit` averages over the *shared*
  champion universe, so a region missing a champion entirely is charged the full deficit for it rather than
  scoring as perfectly covered.
- **The constant `1` is what makes this a balancer rather than a switch.** A fully covered platform keeps its
  even share and its established mains keep being refreshed; the deficit is a bonus on top, capped at 2× a
  covered platform's share. Starving the leader would only invert the imbalance.
- **Self-damping, like the per-champion signal.** As a region fills, its deficit shrinks, its weight decays
  toward 1, and the allocation converges on an even split instead of oscillating.
- **Quotas are floors, not partitions** — the same semantics as `Harvest:NewCandidateShare` (#495) and
  `MatchIngestion:EstablishedMainShare` (#900). A platform that cannot fill its slice releases it, and the
  spill is **round-robin**: handing the whole remainder to whichever platform sorts first is the
  cross-platform ordering again, and would quietly restore what the quotas exist to correct.
- **Nulls-first survives, scoped per platform.** Never-ingested accounts still go first *within* a region; it
  was only ever harmful across them.

Observability is deliberately not part of this: the per-platform balance is visible in the claim's allocation
log line and the `HarvestBudgetExhausted` event, but the admin portal has no region-balance panel yet
(tracked separately). Until it does, a drift like this still has to be inferred from run summaries.

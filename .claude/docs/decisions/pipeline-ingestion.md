# Ingestion pipeline — processes, leases and resilience

Part of the [decision log](../decisions.md). Format: **Decision** — why — `source`.

**A starter-item scan must only trust events that prove ownership, not every event mentioning an item**
(`Data/BuildFacts/StarterItemAnalyzer.cs`, found via #923's matchup filter). The support-quest fallback
scans a participant's full event stream for a completed quest item (Celestial Opposition, Dream Maker,
Solstice Sleigh, Bloodsong) to surface it in the starter slot — necessary because World Atlas is
auto-gifted with no `ITEM_PURCHASED` event. But junglers' timelines carry six to eight `ITEM_DESTROYED`
events *naming* those same completions (an artifact of Riot's event stream, not anything the jungler
owned), and the scan originally treated any reference as proof. Result: a jungler's starter basket read
"Scorchclaw Pup + Bloodsong + Health Potion" — 400 g injected past the 500 g budget. Fixed by only
counting `ITEM_PURCHASED` / `ITEM_UNDO` as ownership for a *completion*; a destroyed root or intermediate
still counts (that is exactly what a support's gifted World Atlas looks like transforming). The general
rule: an event-stream heuristic that infers state from "this item was mentioned" rather than "this item
was proven owned" will eventually be poisoned by a class of player the heuristic wasn't built for.

**The ingestor Worker isolates failures per process.**
Discovery's 40 s total timeout (a regression from deriving the total from attempt count, which Riot's
`Retry-After` easily exceeds) crashed the first process in a plain `foreach`, so **nothing else in the pipeline
ran between May 30 and June 12** — #443.

**A streamed Riot response can fail *after* the resilience handler has waved it through — isolate the
call site.** Since #253 the Riot clients fetch with `HttpCompletionOption.ResponseHeadersRead` and
deserialize off the still-flowing stream. `AddStandardResilienceHandler` decides whether to retry on the
*headers*, so a body that dies mid-stream arrives as a perfectly good 200 and the retry strategy never sees
the failure: it surfaces as a `JsonException` thrown outside the pipeline. In `TimelineIngestionService` one
such truncated timeline aborted `IngestSingleAccountAsync`, rolling back the account's whole per-account
transaction — every match snapshot already ingested in that run was discarded and the account reverted to
queued, for one flaky HTTP body (3 occurrences in prod between 2026-07-21 and 2026-08-09) — #1052.
Fixed by catching the payload-level faults around the *fetch only* and leaving `TimelineIngested = false`,
which hands the match to the existing pending-timeline path for a later run. A consecutive-failure cap
(`MaxConsecutiveTimelineFailures`) still rethrows, so a Riot outage aborts the account instead of reporting a
healthy run that ingested nothing. 👉 The general rule: retry policies cannot cover streamed bodies, so
per-item error isolation belongs at the call site — the same principle as the Worker's per-process isolation
above, one level down.

**A CommunityDragon patch branch that does not exist yet is a transient condition, not a fatal one.**
CommunityDragon mirrors a patch hours-to-days after Riot ships it, so on every patch day the first games on
the new patch reach aggregation while `raw.communitydragon.org/<patch>/` still 404s. `EnsureSuccessStatusCode`
let that escape `CommunityDragonItemMetadataProvider`, and 7 matches on 16.16 aborted **both**
`ChampionPatternAggregation` and `ChampionPowerspikeAggregation` on every ingestor cycle — powerspike fully
stalled, since its batch selector re-picks the same uncommitted batch forever (16 errors on 2026-08-12, one
failure of each process per cycle) — #1107. Fixed by falling back to the `latest` branch (the previous patch
until CommunityDragon catches up, then the new one) and re-probing the real branch every 30 min. Skipping the
affected matches was rejected: `ProcessBatchAsync` flags every match in a batch as `PowerspikeAggregated`
whether or not it contributed, so a skipped match is dropped from the aggregates permanently — stale-by-one-patch
item metadata beats a hole. A non-404 failure still throws; an outage must not be papered over with the wrong
patch's data. Faulted loads are also no longer cached — a `Lazy<Task<…>>` that faults kept rethrowing the
original error for the life of the process, which for the Api means days. 👉 The general rule: an upstream that
publishes on its own schedule needs a degraded answer, not an exception, on the window where it lags.

## Ranks are read from the ladder, not from one account at a time (2026-08-30)

**The ladder endpoints are the primary rank source; the per-account call is the fallback.** `AccountRefresh`
spends one `league-v4/entries/by-puuid` call per account at `BatchSize: 200` a cycle, which caps the whole
fleet at roughly 2 400–4 800 refreshes a day — and that budget is shared with the Riot-ID identity backlog
(#788), whose P0/P0.5 buckets take the entire batch whenever it is large. The visible result was LP that was
days stale. The ladder answers the inverted question far more cheaply: one call returns a whole apex tier, one
paginated call returns ~205 consecutive players of a division, and matching those entries against accounts we
already store is a pure SQL join. `LadderSyncProcess` reads the three apex ladders every cycle (nine calls for
three platforms) and sweeps the tiers below Master incrementally — #1312.

**Sweep depth is bounded by a request budget, not by a tier list.** A full Challenger→Emerald pass over three
platforms is on the order of 3 900 calls (Emerald alone is ~1 100 pages per platform). `MaxRequestsPerRun`
therefore buys sweep *rate*, not sweep *coverage*: the cursor resumes where the previous run stopped, so
configuring a deeper scope costs latency to come round again, never a budget blowout. The rentability rule per
division is `tracked_accounts > population / page_size` — roughly **0.5 % of the division**. Master+ clears it
by a wide margin, Diamond very likely, Emerald is the uncertain step, which is why the run summary reports
entries and matches per tier: the scope is a measurement, not a guess.

**The sweep never inserts accounts.** Seeding every player of every swept division across three regions would
add millions of `riot_accounts` rows and swamp every downstream step. Discovery stays the only intake from the
ladder, and stays scoped to the apex tiers.

**Platforms rotate one page at a time, and the cursor advances before the fetch.** Draining one platform
before the next would mean the last platform never advances once the budget is the binding constraint — the
same region-blind allocation as #1149/#1150. Advancing the cursor first is the #486 lesson: a page that fails
deterministically must not pin the sweep on it forever.

**An account that leaves the swept range needs no special case.** It is simply not seen, so
`LastRankSyncAtUtc` does not advance and `AccountRefresh` picks it up in its normal rotation — which is also
what re-detects a demotion. `AccountRefresh:RankSyncFreshness` moved from 15 minutes to 12 hours in the same
change: at 15 minutes the gate expired long before the next sweep came round, the per-account call was
re-issued anyway, and none of the saved budget was actually reallocated.

## A lease is only kept if something reaps it (2026-09-01)

**`Processing` is a lease state, and the pipeline now enforces the lease.** `MatchClaimService` moves an
account's candidates `Queued -> Processing` and stamps the account's claim; every ordinary exit path settles
them again. A hard stop — an OOM kill, a container restart, a revert that itself failed — has no exit path,
and `MatchIngestionProcess` already documented the intended safety net ("candidates remain Processing until
the claim lease expires") without anything ever applying it to the rows. `MatchIngestion` now reaps expired
claims before it claims, so what a dead run left behind is claimable in the same pass.

**Recovery must not be gated on the membership the failure destroys.** The lease cutoff did exist, but only
inside `SelectClaimableAsync`, which reaches an account through one of two predicates: it holds an active
main, or it holds a `Queued` candidate. An account whose candidates were *all* stuck at `Processing` matched
neither, so it was invisible to the only mechanism that would have settled its rows — the leak sealed itself
and grew monotonically. Production had 1 185 rows across 498 accounts, 386 of them permanently unreachable,
accumulated from 2026-06-13 onward. Whenever a recovery path is filtered by state, check that the state it
filters on survives the failure it recovers from.

**The reaper releases what no live claim stands behind, not what carries an expired one.** The predicate is
negated on purpose: a candidate whose account row is gone has no claim at all, and an `EXISTS` on the expired
shape would leave it `Processing` forever. Both sides take the cutoff from the same
`MatchIngestion:ClaimLeaseMinutes` the claim uses, passed in by the caller, so the reaper cannot decide a
lease is spent while the claim still considers it held. Measured on production: 68 ms per pass at ~864 k
candidate rows, served by the existing `(PlatformId, Status, Score)` and partial claim indexes — no new index
(#1344).

## Jungle first-clear tracking was built, then removed entirely (2026-08-24)

Shipped over #1186–#1195, then deleted: the camp sequence, the storage, the ingestion, the match-detail tab
and the Core camp geometry. The product call is that a first clear which cannot be tracked *completely* is not
worth its surface, and complete tracking is not achievable from Riot's data.

Why it cannot be: `participantFrames` are sampled **once per minute** while a clear runs from the 1:30 spawn to
about 3:15, so the whole clear is covered by two usable samples, and Riot emits **no camp-kill event** (the old
sub-elite buff kills were removed deliberately). Six camps cannot be ordered from two positions. The original
#535 builder credited one camp per frame, which put a hard 6:00 floor under a clear that really ends near 3:15 —
of 148 237 production rows only 84 (0.06 %) ever reached six camps.

The full account, including the measurements, the three bugs found on the way and the two contradictions that
were never resolved, is in **issue #1206**. Read it before proposing this again.

Two general rules the episode paid for, and they outlive the feature:

- **Check a rate a spec asserts before building on it.** "~1 camp/min" was never true; everything downstream
  inherited it. The sampling interval bounds what is knowable, and no inference recovers detail the sampler
  never captured.
- **Do not store what you can derive.** The full-clear time was stored *and* derivable; when the rule behind it
  changed, the derived side healed and 35 % of the stored side silently did not.

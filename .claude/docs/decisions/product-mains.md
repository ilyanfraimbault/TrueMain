# Mains, dedication and candidate intake

Part of the [decision log](../decisions.md). Format: **Decision** — why — `source`.

**Ranked solo/duo (queue 420) is the only queue stored; match history is solo/duo-only by design.**
Prod Postgres reached 68 GB and filled the VPS disk (Mongo SIGSEGV mid-write, API and ingestor down).
Retention only pruned 420, so flex/normal/arena accumulated forever. Every aggregate and query service was
already hard-scoped to 420, so non-420 had no consumer except player match history — accepted — #680.

**Dedication score (0–100) is the signature metric, always scoped to one champion.**
LP measures skill, dedication measures *ownership* of a champion. Weights 0.45 commitment / 0.20 span /
0.20 volume / 0.15 recency. Unfiltered, it scores the player's signature champion; filtered by `championId`,
it scores that one — `docs/dedication-score.md`, #530.

**The `/truemains` leaderboard is strictly `IsMain=true`.**
The `EXISTS` used to short-circuit without champion/position filters, so freshly ingested un-analysed accounts
appeared on a page literally named "Truemains" — Challenger rows with no champion icons — #184.

**Leaderboard games/KDA/WR come from frozen aggregate scopes, not live `match_participants`.**
Retention deletes participants past 2 patches, which collapsed real players to "0 games". Accepted caveats:
counts cover main champions only, and KDA is understated on pre-migration frozen scopes that cannot be
backfilled — #719.

**Inactive mains are retired via champion-mastery `lastPlayTime`; intake favours depth over breadth.**
Dead mains sat at rank #1 with 0 games while burning match-v5 calls. Mastery-v4 is one call per account
versus pulling match history. Past a minimum coverage per champion, more games from real mains beats more
distinct mains. Rows are deactivated (`IsActive=false`), **never deleted**, so a returning player is not
rediscovered from scratch — #900.

**Candidate scoring is scarcity-weighted and the `IsMain` threshold is coverage-adaptive (0.20 → 0.12).**
Games per champion spread 68× (Ezreal 2039 vs Amumu 30) and the binding constraint was retention, not
discovery — Amumu had 23 validated candidates and 0 `IsMain` under a flat 20% floor. Scarcity fills the
bucket, the adaptive floor stops it leaking, and sub-0.20 mains are flagged `IsExtendedSample` so the UI can
label them honestly — #407.

**Thin samples degrade, they don't 404.**
A champion shown as a main on a profile dead-ended with "Not enough games" on its build page, because the two
views count different populations. The min-games floor became a patch-*preference*; 404 only when no
aggregate exists at all — #762.

**The account explorer (#1032) reports raw score inputs, not score components — and never calls Riot.**
The issue asked for a candidate's score "and its components" (recency/rank/points/scarcity). `main_candidates`
persists only the final `Score` double; the blend lives in `ScoringProcess.ComputeScore` inside the Ingestor,
and `ScoringOptions` (its weights) is Ingestor-only config the `Api` project cannot see. Recomputing the
breakdown at read time was rejected: champion scarcity is a live snapshot of current coverage, so a
recomputed decomposition would silently disagree with the `Score` sitting next to it on the same row —
wrong on a page built specifically to be trusted for diagnosis. The endpoint instead exposes the persisted
inputs (`LastPlayTimeUtc`, `ChampionRankInMasteryTop`, `ChampionPoints`, `ObservedGames`) and states plainly
that the decomposition is not stored. Same reasoning kept the endpoint **database-only**: the API has no
Riot client, so an unrecognised Riot ID renders as `NeverDiscovered` (200), not a 404 — the read never claims
to know whether the Riot ID exists at Riot, only whether the pipeline has ever recorded it.

**`MainActivity` deactivation carries no persisted reason, so the account explorer says so rather than guessing.**
`MainActivityProcess` writes exactly two fields — `MainChampionStat.IsActive` and
`RiotAccount.LastActivityCheckAtUtc` — collapsing two distinct causes (mastery `lastPlayTime` older than the
inactivity window, or no mastery entry for the champion at all) into one boolean, with no `DeactivatedAtUtc`
or reason column (#900). A failed mastery lookup leaves both fields untouched, so `IsActive = false` is only
a *confirmed* retirement when `LastActivityCheckAtUtc` is recent — an older stamp means the last check that
actually completed predates the flag flip, or never ran to confirm it. Adding a reason column was considered
and deferred: it needs an Ingestor write-path change and a migration, for a fact the pipeline does not
currently observe (mastery-v4 returns "no entry", not "entry expired *because* X"). The read-model names both
possible causes and reports the confirmation timestamp instead.

## A main whose matches expired is dated, not deleted and not hidden (2026-08-24)

A profile advertised "Graves — 10 games — 33%" while the champion page behind it answered "No personal
build breakdown yet". The instinct — "a min-sample floor is hiding thin data, make it render with a
warning instead" — was wrong twice, and both corrections are worth keeping:

- **There is no min-sample gate on that path, and there never was.** `ChampionScopeLoader` documents its
  floor as "a *preference*, not a gate", and `ChampionBuildsQueryService` renders an aggregate of any size,
  flagging thin ones with `MinSampleMet=false`. A 404 there means *zero scope rows*, never "too few games".
  Before proposing to soften a floor, check whether the floor is what answered.
- **The "10 games" was not an aggregate.** It came from `main_champion_stats`, a snapshot dated three weeks
  earlier. Two different stores with two different retention rules were being read as if they agreed.

The actual defect: `MainAnalysisProcess`'s #825 thin-sample guard (`hasEstablishedMain && newTotalMatches <
MinMatchesToEvaluate` → return early) also catches `newTotalMatches == 0`. Raw matches age out of
`MatchDataRetention` on their own — two patches in prod — so an account nobody re-ingested drops to zero
participants, the condition becomes permanently true, and the row is immortal. **Thin is insufficient
evidence; zero is absent evidence**, and the guard conflated them.

Fix: `IsSampleRetired`, set on the zero branch, cleared by any later cycle that upserts from a real sample.

- **Not deleted.** Deleting would drop a player off the leaderboard the moment their matches expire, which
  in prod is two patches of inactivity — it would empty the leaderboard of exactly the specialists it exists
  to track.
- **Not hidden.** The figures are a real past measurement, so the profile keeps them and dates them
  (`20 games · as of 2 Jul`). What was wrong was never the number, only the claim that it was current.
- **Not `IsActive`.** That flag comes from Riot mastery `lastPlayTime` and answers "does the player still
  play this champion?". A row is routinely active *and* retired: they still main it, we no longer hold the
  games.

Not in scope, and deliberately so: the empty champion page is **preprod behaving as configured**.
`compose.preprod.yaml` sets `MatchDataRetention__AggregateRetainedPatchCount: "2"` while prod leaves it at
the default `0` (disabled — old-patch aggregates are the site's patch history, #466), so that build renders
in prod. Preprod stays on its diet by choice; remember it when auditing preprod for missing data.

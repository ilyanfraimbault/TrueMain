# Dedication score

TrueMain's signature metric: **how devoted a player is to one champion**, on a
0..100 scale. It is what makes a "truemain" different from a high-LP account —
LP measures how good you are, dedication measures how much the champion is
*yours*.

- Formula: [`backend/Core/Truemains/DedicationScore.cs`](../backend/Core/Truemains/DedicationScore.cs)
  (pure, unit-tested in `backend/tests/TrueMain.UnitTests/DedicationScoreTests.cs`).
- Data loading: [`backend/Api/Services/Truemains/MainDedication.cs`](../backend/Api/Services/Truemains/MainDedication.cs).
- Exposed on `GET /truemains/{nameTag}/profile` and on every row of
  `GET /truemains`, which also accepts `?sort=dedication`.

## The formula

```text
score = 100 × ( 0.45 × commitment
              + 0.20 × span
              + 0.20 × volume
              + 0.15 × recency )

commitment = clamp01( (playRate − 0.12) / (1 − 0.12) )
span       = clamp01( patchSpan / 6 )
volume     = clamp01( ln(1 + careerGames) / ln(1 + 200) )
recency    = clamp01( 0.5 ^ (daysSinceLastGame / 21) )
```

Each component is normalised to `0..1`, the weights sum to `1`, so the score
spans the full `0..100` range. It is rounded to one decimal — the number the
leaderboard sorts on is the number it prints.

### Which champion is scored

The score is always about **one champion**, not the whole account:

- On the profile, and on an unfiltered leaderboard: the player's **signature
  champion** — their most-played main (`PlayRate` desc, then `ChampionMatches`
  desc, the same order every other truemain surface uses to pick a top champion).
- On a leaderboard filtered by `championId`: **that** champion, so "the most
  dedicated Yasuo players" ranks Yasuo dedication rather than each player's
  unrelated top main.

## The four components

| Component | Weight | Input | Source |
|---|---|---|---|
| `commitment` | 0.45 | share of the player's recent ranked games on the champion | `main_champion_stats.PlayRate` |
| `span` | 0.20 | distinct patches we have seen them play it on | `COUNT(DISTINCT GameVersion)` over `champion_aggregate_scopes` |
| `volume` | 0.20 | tracked ranked games on the champion | `SUM(Games)` over the same scopes |
| `recency` | 0.15 | days since the last tracked game on it | `MAX(LastGameStartTimeUtc)` over the same scopes |

### Why a weighted mean, not a product

No single missing signal should zero a player out.

- A genuine one-trick whose aggregates have not been built yet (`span` and
  `volume` still 0) keeps the commitment points they earned.
- A long-tracked veteran who took a break keeps their span and volume while
  only the recency term decays.

That last property also matters operationally: recency is the one component that
moves when *nothing about the player* changes. If ingestion stalls (a dead Riot
key, a crashed process), a product-shaped formula would collapse every score at
once. With a 0.15 weight, a full stall costs everyone the same 15 points and the
ranking survives.

### Why these shapes

**`commitment` is rescaled from 0.12, not from 0.** Main analysis relaxes its
play-rate threshold down to `MainAnalysis:PlayRateFloor` (0.12) for
under-covered champions, so no classified main can sit under it. Rescaling
spends the whole 0..1 range on the interval that actually occurs instead of
leaving the bottom eighth of the scale unreachable.

**`span` counts patches, not calendar days.** A player who stuck with the
champion across six patches has survived six rounds of balance changes, which is
the honest measure of "still their champion" — calendar time would reward an
account that was simply *discovered* early.

**`volume` is logarithmic.** The difference between 10 and 60 games says far
more about devotion than the difference between 400 and 450, and a log curve
keeps a high-volume outlier from flattening everyone else.

**`recency` is a half-life, not a cliff.** A player slides down the board
gradually instead of dropping off it the day after an arbitrary cutoff. Three
weeks is long enough to ignore a holiday, short enough that a stale main reads
as stale.

## Calibration constants

| Constant | Value | Meaning |
|---|---|---|
| `CommitmentFloor` | 0.12 | play rate at which `commitment` reads 0 |
| `SpanTargetPatches` | 6 | patch count at which `span` saturates |
| `VolumeTargetGames` | 200 | career games at which `volume` saturates |
| `RecencyHalfLifeDays` | 21 | days of inactivity that halve `recency` |

They live as `public const` on `DedicationScore` — a single source of truth, and
the calibration surface. Changing one changes every score, including the
leaderboard order, so treat a change as a product decision and not a tweak.

## Known limits

- **We can only measure what we have tracked.** `span` and `volume` come from
  TrueMain's own aggregates, so an account discovered last week scores low on
  both even if the player has one-tricked for years. This is a floor that lifts
  as the account is observed, not a permanent verdict.
- **Retention does not erode the score.** The career figures deliberately read
  `champion_aggregate_scopes` rather than `match_participants`: retention
  hard-deletes participants beyond the last couple of patches, while old-patch
  scopes stay frozen (#466). A veteran's history therefore survives.
- **Queue 420 only.** Career totals count ranked solo/duo, matching the queue
  main analysis measures the play rate over.

## Ranking by dedication (`?sort=dedication`)

The score is computed **at read time**; there is no materialised column, so
there is no index to order by. The leaderboard therefore:

1. resolves one row per eligible account (`DISTINCT ON` over
   `main_champion_stats`, joined laterally to that account's aggregate scopes) —
   the same population predicate the default ranking counts;
2. scores each candidate with the pure function and sorts in memory (score desc,
   then account id as a stable tiebreak);
3. slices the page and hydrates only those ~25 rows, exactly as the default
   ranking does.

The candidate scan is capped (`MaxDedicationCandidates`, 50 000, ordered by
descending play rate). Below the cap the ranking is exact. Hitting it logs a
warning — that is the signal that the score has outgrown a read-time
computation and should become a materialised column maintained by the ingestor
(with the matching EF migration and a regenerated compiled model), the way
`riot_accounts."Score"` is.

The default (`?sort=rank`) path is unchanged: it still counts and pages on the
materialised rank score, and only pays a small extra query to attach the
dedication cell to the page's rows.

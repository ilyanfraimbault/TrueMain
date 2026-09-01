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

`championId` is the **only** filter that re-points the score. Every other
leaderboard filter — `position`, `otpOnly`, the `MinRankedGames` floor — decides
which players are *eligible* and never which of their champions is scored:

| Filter | Gates membership | Re-points the score |
|---|---|---|
| `championId` | yes | **yes** |
| `position` | yes | no |
| `otpOnly` | yes | no |
| `MinRankedGames` | yes | no |

So a top-laner who also mains a mid champion keeps their **top-lane** score under
`?position=MIDDLE` — the filter surfaces them because they play the lane, it does
not decide what they are known for. Two reasons this is the right side of the
trade:

1. It matches what a lane filter already means on this leaderboard — "every
   player who plays that position on a main champion at least `MinShare` of the
   time", a statement about players, not about champions. The row's leading
   champion icon is position-blind for the same reason, so the dedication cell
   and the icon next to it stay about the same champion.
2. It makes the score a property of the player, not of the request. The
   leaderboard column, the profile card and both sort orders all agree for the
   same account.

That last property is enforced structurally rather than by convention: every
surface scores through the one `MainDedication.FetchAsync` entry point, whose
signature takes `championId` and nothing else that could reach the pick. An
earlier revision folded the filters into the same `DISTINCT ON` that chooses the
champion, which made `?position=X` score a *different* champion depending on
which sort was active; `TruemainsDedicationApiIntegrationTests` now pins the
invariant.

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
| `CommitmentFloor` | 0.12 (default; the live value is `MainAnalysis:PlayRateFloor`) | play rate at which `commitment` reads 0 |
| `SpanTargetPatches` | 6 | patch count at which `span` saturates |
| `VolumeTargetGames` | 200 | career games at which `volume` saturates |
| `RecencyHalfLifeDays` | 21 | days of inactivity that halve `recency` |

All four live as `public const` on `DedicationScore`, and they are the calibration
surface: changing one changes every score, including the leaderboard order, so
treat a change as a product decision and not a tweak.

Three of them — `SpanTargetPatches`, `VolumeTargetGames`, `RecencyHalfLifeDays` —
are closed constants, and the const *is* the single source of truth. The
commitment floor is not. `DedicationScore.Compute` and `DedicationScore.Commitment`
take it as an optional parameter, and every real caller passes the live
`MainAnalysis:PlayRateFloor` instead of the default — the leaderboard query
service and `MainDedication.Project` both read it from configuration. The const is
only the fallback, and it is also that option's own default. Editing
`CommitmentFloor` alone therefore changes no score in production: the floor
follows the mains-classification configuration, deliberately, so that retuning
what counts as a main moves the dedication scale with it (#869).

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
there is no index to order by. The leaderboard therefore runs two deliberately
separate phases:

1. **Eligibility** — the ids of the accounts the filters admit, with every filter
   landing on the same `main_champion_stats` row (so `?championId=X&position=Y`
   means "has an X main played in Y"). This predicate is the one the default
   ranking counts with, so the total and the ranked slice always agree.
2. **Scoring** — the *same* `MainDedication.FetchAsync` the rank-sorted
   leaderboard and the profile call, which picks the signature champion and
   measures its career. The filters from phase 1 do not reach it.

Then the candidates are sorted in memory (score desc, account id as a stable
tiebreak), the page is sliced, and only those ~25 rows are hydrated — exactly as
the default ranking does.

Splitting the phases is what guarantees the "same player, same score" invariant
above. It costs one extra round trip per uncached dedication-sorted request,
which is the right trade for making the whole class of filter-dependent scoring
bugs unrepresentable.

The ranked candidate set is cached per **filter shape** — region, champion,
position, ranked-games floor, OTP-only — and deliberately *not* per page, since
paging is the normal way people read a leaderboard and the per-page response
cache alone would repeat the whole scan on every page change. So the scan and
the scoring pass are paid once per sorted board, not once per page, and pages
sliced from one cached ranking cannot repeat or skip a row when the data shifts
underneath. The entry is charged against the shared cache's size budget in
proportion to the population it holds (roughly one unit per 100 scored
accounts, where one unit is about what a page response costs); a ranking that
would exceed an eighth of the budget is not cached at all rather than evicting
every other surface.

The eligibility scan is capped (`MaxDedicationCandidates`, 50 000, ordered by
descending play rate on each account's best matching main, so the rows dropped
are the least committed). Below the cap — i.e. always, in practice — the ranking
is exact. Truncating logs a warning: that is the signal that the score has
outgrown a read-time computation and should become a materialised column
maintained by the ingestor (with the matching EF migration and a regenerated
compiled model), the way `riot_accounts."Score"` is.

Because that warning is a call to action, it is made exact rather than
approximate: the scan asks for `limit + 1` ids and treats only "more than the
cap came back" as truncation, so a population landing precisely on 50 000 does
not fire it. The probe row is dropped before scoring and never reaches the
ranking or a page.

Two details of the capped path, both deliberate:

- **`total` stays the true population.** It is a count of eligible players, not
  of reachable rows, and the homepage renders it as a "truemains tracked"
  figure — so the capped path pays one extra count rather than reporting 50 000.
  The unreachable tail pages then come back empty, which is the honest failure
  mode: a page that can't be filled beats a population figure that is quietly
  wrong.
- **The truncation order is approximate under a lane filter.** The play rate the
  cap sorts on is that of the main satisfying the filter, which need not be the
  top main that actually gets scored. Ordering by the true top main would need a
  correlated `MAX` per candidate — the unbounded work the cap exists to avoid.
  Every account that survives the cap is still scored on its true signature
  champion, so this can only shift which far-tail rows exist, never a score.

The default (`?sort=rank`) path is unchanged: it still counts and pages on the
materialised rank score, and only pays a small extra query to attach the
dedication cell to the page's rows.

# Truemain score

TrueMain's signature metric: **how much of a truemain a player is on one
champion**, on a 0..100 scale. It is what makes a "truemain" different from a
high-LP account — LP measures how good you are, this score measures how much the
champion is *yours*.

It shipped as the "dedication score" (#530) and was reworked and renamed by
issue #1701. The **code and the wire keep the `dedication` name** — `DedicationScore`,
`DedicationReadModel`, the `dedication` payload field and `?sort=dedication` —
so shared leaderboard links keep working; only what a reader sees says
"Truemain score".

- Formula: [`backend/Core/Truemains/DedicationScore.cs`](../backend/Core/Truemains/DedicationScore.cs)
  (pure, unit-tested in `backend/tests/TrueMain.UnitTests/DedicationScoreTests.cs`).
- Data loading: [`backend/Api/Services/Truemains/Leaderboard/MainDedication.cs`](../backend/Api/Services/Truemains/Leaderboard/MainDedication.cs).
- Mastery inputs: written by `MainActivityProcess` (`backend/Ingestor/Processes/MainActivityProcess.cs`).
- Exposed on `GET /truemains/{nameTag}/profile` and on every row of
  `GET /truemains`, which also accepts `?sort=dedication`.

## The formula

```text
score = 100 × ( 0.55 × commitment
              + 0.30 × mastery
              + 0.15 × masteryRank )

commitment  = clamp01( (playRate − 0.12) / (1 − 0.12) )
mastery     = clamp01( ln(masteryPoints / 50 000) / ln(3 000 000 / 50 000) )
masteryRank = 1 / rank            (0 when unknown)
```

Each component is normalised to `0..1`, the weights sum to `1`, so the score
spans the full `0..100` range. It is rounded to one decimal — the number the
leaderboard sorts on. The payload also ships each part's contribution in score
points (`parts[]`: `points` out of `maxPoints` = weight × 100), so the UI prints
figures that add up to the score instead of normalised bars.

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

## The inputs

Every input lives on the signature champion's `main_champion_stats` row — the
score reads nothing else.

| Part | Weight | Input | Written by |
|---|---|---|---|
| Play rate (`commitment`) | 0.55 | share of the player's recent ranked games on the champion | main analysis → `PlayRate` |
| Mastery | 0.30 | Riot champion-mastery points on the champion | `MainActivityProcess` → `MasteryPoints` |
| Mastery rank | 0.15 | the champion's place in the player's mastery by points (1 = most-played ever) | `MainActivityProcess` → `MasteryRank` |

Also shipped, but **not scored**: `MasteryLastPlayUtc` as "last played N days
ago", and `IsOtp` as the verdict.

## What it answers, and what it deliberately doesn't

Two questions: does the player give this champion their games **now** (play
rate), and has it been **theirs for a long time** (mastery points and rank).

**Nothing depends on how long TrueMain has tracked the account.** The previous
formula spent 40% of its weight on `span` (distinct *tracked* patches) and
`volume` (*tracked* games) from `champion_aggregate_scopes`. Both grew with our
observation window, not with the player: a years-long one-trick discovered last
month scored like a dabbler. Riot mastery is lifetime and all-queue, so it
measures the player from their first read.

**Activity is a gate, not a component.** The old `recency` term (15%) mostly
handed every main the same free points — play rate is already measured over
recent games, so a main has always played recently — and it was the one term
that moved when ingestion stalled. Inactive mains are already excluded upstream
through `IsActive` (mastery `lastPlayTime`, #900), so the score no longer decays;
the last-played date is shown as a fact.

**The verdict is `IsOtp`, not a score band.** The badge next to the name, the
`otpOnly` filter and the verdict pill in the tooltip all read the same
`main_champion_stats.IsOtp` flag (play rate ≥ `MainAnalysis:OtpPlayRateThreshold`),
so they cannot disagree. The old Devoted/Committed/Invested/Casual/Dabbling bands
were a second vocabulary for the same idea and were dropped.

### Why these shapes

**Play rate stays dominant and is rescaled from 0.12, not from 0.** Main analysis
relaxes its play-rate threshold down to `MainAnalysis:PlayRateFloor` (0.12) for
under-covered champions, so no classified main can sit under it. Rescaling spends
the whole 0..1 range on the interval that actually occurs. The UI never draws
the rescaled value against the raw percentage: it prints the raw play rate and
the points it earned.

**Mastery is logarithmic between 50k and 3M points.** Going from 100k to 200k
points says as much about ownership as going from 1M to 2M. The bounds bracket
the mains population measured when the formula was calibrated: 50k sits near
its bottom and 3M past its 90th percentile, so the curve spreads the population
instead of saturating it.

**Mastery rank is `1 / rank`.** Whether this is the champion the player has
played most, ever, is the sharpest single signal of a main; the reciprocal keeps
a second or third champion meaningful without letting it rival the first.

**Unread mastery scores 0 on those parts and says so.** A main `MainActivity`
has not reached yet shows "not checked yet" rather than an invented value.
Accounts with such a main are checked first (`GetAccountsForActivityCheckAsync`),
and the migration that added the columns seeded them from the mastery Discovery
had already stored on the matching candidate, so this is a short transition, not
a steady state. A champion with **no** mastery entry is a measured 0 points and
no rank — distinct from null.

## Calibration constants

| Constant | Value | Meaning |
|---|---|---|
| `CommitmentWeight` / `MasteryWeight` / `MasteryRankWeight` | 0.55 / 0.30 / 0.15 | part weights |
| `CommitmentFloor` | 0.12 (default; the live value is `MainAnalysis:PlayRateFloor`) | play rate at which commitment reads 0 |
| `MasteryFloorPoints` | 50 000 | mastery points at which `mastery` reads 0 |
| `MasteryTargetPoints` | 3 000 000 | mastery points at which `mastery` saturates |

All live as `public const` on `DedicationScore`, and they are the calibration
surface: changing one changes every score, including the leaderboard order, so
treat a change as a product decision and not a tweak.

The commitment floor is the one that is not closed. `DedicationScore.Compute`
and `DedicationScore.Commitment` take it as an optional parameter, and every real
caller passes the live `MainAnalysis:PlayRateFloor` instead of the default. The
const is only the fallback, and it is also that option's own default. Editing
`CommitmentFloor` alone therefore changes no score in production: the floor
follows the mains-classification configuration, deliberately, so that retuning
what counts as a main moves the score with it (#869).

## Known limits

- **Mastery is as fresh as the last activity check.** `MainActivityProcess`
  re-reads it every `MainActivity:RecheckAfterHours` at best; a full pass over
  the population takes longer, bounded by its batch size and run interval.
- **Mastery counts every queue.** Points earned in normals or ARAM count towards
  ownership. That is intended: the question is whether the champion is the
  player's, not how they queue. Play rate stays ranked solo/duo only.

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
   reads its inputs. The filters from phase 1 do not reach it.

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

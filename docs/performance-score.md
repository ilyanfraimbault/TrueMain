# Performance score

TrueMain's **per-match** metric: how well one player played one game, on a
0..100 scale. Where the [dedication score](dedication-score.md) is about a
player's relationship to a champion over months, this one is about a single
game — and it is what decides the **MVP / ACE** accolade on a match row.

- Formula: [`backend/Core/Lol/Performance/PerformanceScore.cs`](../backend/Core/Lol/Performance/PerformanceScore.cs)
  (pure, unit-tested in `backend/tests/TrueMain.UnitTests/PerformanceScoreTests.cs`).
- Ranking / accolades: [`MatchPerformanceRanker.cs`](../backend/Core/Lol/Performance/MatchPerformanceRanker.cs).
- Input assembly: [`backend/Api/Services/Truemains/PerformanceInputs.cs`](../backend/Api/Services/Truemains/PerformanceInputs.cs).
- Surfaced on `GET /truemains/{nameTag}/matches` (score + placement + MVP/ACE per
  row), `GET /truemains/{nameTag}/matches/{matchId}` (all ten participants) and
  `GET /truemains/{nameTag}/champions/{championId}/performance` (the aggregate,
  with the per-component breakdown).

## The shape

```text
score = 100 × Σ(weight × value) / Σ(weight)      over the *available* components
```

The result is **rounded to the nearest integer, `AwayFromZero`** — not the .NET
default banker's rounding — so the published score is the one you reproduce by
hand from the weights. The score is an `int`; there is no decimal part to compare
against.

Nine components, each normalised to `0..1`. The weights are per-role and sum to
100, so a game with every component available spans the full `0..100` range.

The one structural rule: **a component whose input we do not have is dropped,
and its weight is redistributed over the survivors** — never scored as a zero.
A game with no timeline rows, a remake with no team kills, a 14-minute surrender
with no @20 mark: none of them punish the player for a gap in our data. The
distinction matters enough that the input carries it explicitly — for roams,
`null` means "this match has no kill-position coverage" while `0` means "the
match is covered and the player never left their lane", and only the second is
graded.

## The nine components

| Component | Input | Normalisation |
|---|---|---|
| `combat` | `(kills + assists) / max(1, deaths)` | linear to a KDA of 6.0 = 1 |
| `killParticipation` | `(kills + assists) / teamKills` | clamped to 0..1 |
| `damageShare` | share of the team's damage to champions | band 5% → 0, 35% → 1 |
| `goldShare` | share of the team's earned gold | band 10% → 0, 30% → 1 |
| `farming` | CS per minute | against a per-role reference |
| `vision` | vision score per minute | against a per-role reference |
| `laning` | leads over the lane opponent at the marks ≤ 15 | centred, see below |
| `midGame` | the same at the marks > 15 | centred, see below |
| `roam` | early kill participations outside the player's own lane | against a per-role reference count |

### The lead curve

`match_participant_timeline_snapshots` stores five canonical marks — 5, 10, 15,
20 and 30 minutes. For every mark that **both** the player and their lane
opponent have a snapshot at, we take the gold / cs / xp lead and grade it:

```text
mark(minute) = 0.50 × centred(goldDiff, 100 × minute)
             + 0.25 × centred(csDiff,     2 × minute)
             + 0.25 × centred(xpDiff,   100 × minute)

centred(diff, span) = clamp01( 0.5 + diff / (2 × span) )
```

Two deliberate choices there.

**The span is proportional to the minute.** A 1 000 gold lead is dominant at 10
minutes and ordinary at 30, so a fixed span would either flatten the early game
or saturate the late one. At minute 15 the spans work out to ±1 500 gold, ±30 cs
and ±1 500 xp — *exactly* the constants the single-@15 model used before, so this
generalises the old calibration rather than replacing it.

**A dead-even lane sits at 0.5, not 0.** The component measures a duel, and
drawing a duel is an average result, not a failure. This is why a player whose
game is otherwise excellent scores *lower* with an even lane than with no lane
data at all — 0.5 drags an 0.86 average down, a dropped component leaves it
alone. That is the honest reading, and the tests pin it.

The marks are then folded per phase, each weighted by its own minute:

```text
laning  = Σ(minute × mark(minute)) / Σ(minute)      for minute ≤ 15
midGame = Σ(minute × mark(minute)) / Σ(minute)      for minute > 15
```

So inside the laning phase the state of the lane at 15 counts three times what it
did at 5 — where the lane *ended up* matters more than where it started. And
splitting at 15 gives the model a second, separate question: once the lane broke,
did the advantage survive? A game that ends before minute 20 simply has no
`midGame` mark and drops the component.

### Roam

Out-of-lane kill participations come from `match_participant_kill_positions`, a
deliberately bounded table (kill participations only, early game only). Each one
is classified with `LolMap.IsRoam` — the same geometry the champion roam panel
uses: a play in a *different* lane, the *enemy* jungle or the *enemy* base counts;
the river and your own side's jungle do not, because they are ordinary lane-phase
movement.

**JUNGLE is excluded**, with a weight of 0. A jungler has no own lane, so every
gank would read as a roam and the component would be a free 100%.

## Role weights

| | Combat | KP | Damage | Gold | Farm | Vision | Laning | MidGame | Roam | cs/min ref | vision/min ref | roam ref |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| TOP | 20 | 14 | 16 | 7 | 14 | 5 | 12 | 7 | 5 | 9.0 | 0.9 | 1.5 |
| JUNGLE | 18 | 18 | 14 | 7 | 14 | 7 | 12 | 10 | 0 | 6.5 | 1.2 | — |
| MIDDLE | 20 | 14 | 18 | 7 | 14 | 5 | 10 | 6 | 6 | 9.0 | 0.9 | 2.5 |
| BOTTOM | 20 | 12 | 20 | 7 | 16 | 4 | 10 | 8 | 3 | 9.5 | 0.8 | 1.0 |
| UTILITY | 18 | 20 | 7 | 4 | 5 | 24 | 8 | 6 | 8 | 2.0 | 2.4 | 2.5 |
| *neutral* | 20 | 16 | 16 | 7 | 12 | 7 | 10 | 8 | 4 | 8.0 | 1.0 | 2.0 |

Each row sums to 100. An empty or unrecognised `teamPosition` (ARAM, an unparsed
role, a remake) gets the neutral profile — roughly the average of the five lanes,
so nothing is graded on a role assumption we cannot back.

The per-role references are what make the same numbers mean different things:
1.6 cs/min and 2.5 vision/min is an excellent support game and a dreadful mid
one, and the model says so without a special case.

Junglers carry the heaviest `midGame` weight (10) because their lead components
compare them to the enemy jungler, a comparison that stays meaningful long after
the lanes have broken. Supports carry the heaviest `roam` weight (8) and the
heaviest `vision` weight (24) for the obvious reason, and the lightest gold and
farm weights, because grading a support on income is grading them on the item
they bought to *not* take income.

## Why a weighted mean over available components

Same reasoning as the dedication score: no single missing signal should zero a
player out, and no single blowout stat should decide the game.

- **Missing never means bad.** Roughly the whole point of the drop-and-
  redistribute rule. Timeline coverage is not uniform across our history — a
  scoring model that read a gap as a 0 would rank old, thinly-ingested matches
  below new ones for reasons that have nothing to do with how they were played.
- **Combat is capped at a KDA of 6.** A 20/0/10 line is already full marks; the
  next ten kills buy nothing. Without the cap one component would swamp the other
  eight and the score would just be KDA with extra steps.
- **Kill participation is clamped at 1.** Shared assists let `kills + assists`
  exceed the team's kill total; that is a quirk of the counting, not a
  performance.
- **Gold share is banded tighter than damage share and weighted lower.** Passive
  income compresses gold share, and the two are correlated — double-counting the
  same "I was fed" signal at full weight would be double-dipping.

## Placement, MVP and ACE

`MatchPerformanceRanker` sorts a match's ten entries by score descending, then
takedowns descending, then deaths ascending, then participant id ascending. The
last key guarantees a **total** order, so equal scores still produce distinct,
stable placements instead of shuffling between requests.

- **MVP** = the best-placed participant among the winners.
- **ACE** = the best-placed among the losers.

Exactly one of each exists in a normal 5v5.

Historically the match-history feed derived these from a raw KDA proxy while the
detail panel behind the same row used the real scorer, so a row could badge a
player MVP and the expanded panel disagree. Both surfaces now build their inputs
through the one `PerformanceInputs` entry point and run the same scorer, which
makes that class of bug unrepresentable rather than merely fixed.

## Deliberate exclusions

- **No objectives.** Dragon / baron / turret takedowns are exposed by Riot but
  **not ingested** — there is no column to read. Adding them means a schema
  change *and* a backfill that cannot happen (we do not keep raw timelines), so
  every historical match would drop the component while new ones kept it. That
  is a worse metric than not having it.
- **No ward counts, no damage taken, no heal/shield.** Same reason: not stored.
  Vision is already represented through the end-of-game vision score, which
  folds wards placed, cleared and denied into one number Riot computes.
- **No win bonus.** The score grades the individual. Winners already score higher
  organically — through KDA, farm, shares and the lead components — and adding an
  explicit bonus would turn "who played well" into "who was on the better team".
- **No peer baseline.** The score is absolute, not percentile: it says *this game
  graded 78*, not *this game was better than 78% of Ahri games in Emerald*. The
  peer-relative view needs a role + rank baseline aggregation that does not exist
  yet — that is the remaining phase of #918.

## Aggregating over a player's games

`GET /truemains/{nameTag}/champions/{championId}/performance` averages the score
over the player's **20 most recent** ranked games on the champion.

- It is a **form** metric, not a career one — and the window is also what keeps
  the read cheap. Every graded match needs its ten participants, its timeline
  marks and its kill positions, and Postgres runs these single-threaded
  (`max_parallel_workers_per_gather = 0`).
- Below **5 graded games** every average is suppressed and the response carries
  only the counts, so the page renders an honest "not enough games yet" instead
  of a confident number built on two games. Five is the same floor the
  player-scoped build panel uses; the same page should not call five games enough
  to name a build but too few to grade a performance.
- Each component is averaged over **the games it was available in**, and that
  count is reported next to the value. A game with no timeline coverage lowers
  the laning component's *sample*, never its *average*.
- `topOfTeamRate` is the share of graded games the player topped their own side
  in (MVP on a win, ACE on a loss).

## Calibration

Every constant lives on `PerformanceScore` (the spans, bands and caps) or in
`RoleProfile.For` (the weights and per-role references). Changing one changes
every score on the site, including which row wears the MVP badge — treat it as a
product decision, not a tweak, and expect to update the golden vector in
`PerformanceScoreTests` in the same commit.

The golden vector exists precisely so that a change is loud: it is a hand-
computed reference stat line with the arithmetic written out in the test, and it
fails the moment the model drifts from what this document says.

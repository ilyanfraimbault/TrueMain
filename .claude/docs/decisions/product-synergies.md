# Champion synergies

Part of the [decision log](../decisions.md). Format: **Decision** — why — `source`.

**Champion synergy is observed *minus expected* win rate, never the raw pair win rate.**
Ranking pairings by raw win rate just re-prints the tier list — two strong champions win together because
they are strong. Expected is combined in log-odds space (`Core.Lol.Synergy.SynergyMath`) rather than by adding
percentages, which is unbounded (two 70% champions would "expect" 90%) — #922.

**The expected value needs two different baselines plus a cohort intercept — one baseline would bias every number.**
The queried side is a tracked truemain on their signature champion (win rate well above 50%); the partner side is
whoever happened to share the game (near the population mean). `champion_synergy_baseline_stats` therefore stores
`SELF` and `ALLY` rows separately, and each ally term is expressed relative to the cohort rate. Without that
intercept every extra teammate would shift the expectation up by a constant and *every* synergy would read
negative. Both baselines are folded by the same process, from the same matches, as the pair rows — deriving them
from another aggregate would compare cohorts that do not match — #922.

**Trios are computed on demand from a chosen pair; the triple space is never pre-aggregated.**
The pair grain is bounded by (champion × lane × partner × lane); adding a third champion and lane multiplies that
into a space that is almost entirely empty, and the few populated cells carry single-digit samples. The live query
narrows to the games the duo actually shared *before* touching the third dimension, so it stays bounded by the
pair's game count rather than the champion's — which matters under `max_parallel_workers_per_gather=0`. Accepted
consequence: the trio slice sees only the retention window while the duo slice reads an aggregate that also holds
frozen patches, so their game counts differ — `pairGames` is returned explicitly rather than reused from the duo
response — #922.

**Synergies floor on a share of the champion's games *and* on whether the partner plays that lane at all.**
The three games floors below were not enough. On production, Viego JUNGLE's four best synergies were pairings of
**21 to 26 games out of 8 202** (0.26%), led by a **+24.7% "synergy" resting on 21 games** — and the very top line
was **Sylas BOTTOM**, which is not a role Sylas plays. Two separate defects wearing the same shirt, so two
separate filters: `MinSynergyPlayRate` (1%, combined with `MinSynergyGames` by taking the larger, exactly like
#1087's matchup floor) for pairings that are merely rare, and `MinSynergyPartnerLanePlayRate` (10% of that
partner's ally games across every lane) for pairings that are *impossible*. The share floor is set at twice the
matchup one because synergy is a difference of two rates and carries the sum of their error — the same reasoning
that already put `MinSynergyGames` above `MinMatchupGames`. After both, the list starts at Darius TOP over 271
games and still holds 131 of 223 partners.
↳ The lane share is computed off the `ALLY` side of the baselines already in memory, **not** off the pairing rows:
those are filtered to one champion's teammates and exclude its own lane, so a share derived from them would read
Udyr as a 100% toplaner on any jungler's page purely because his jungle games cannot appear there. The trio path
gets the lane filter but **no** share floor — a trio's sample is a subset of its duo's, which is why
`MinSynergyTrioGames` already sits *below* `MinSynergyGames` — #1090.

**Synergy carries three separate sample floors, and a thin sample yields no entry rather than a hedged one.**
`MinSynergyGames` (20) is deliberately above the matchup floor: synergy is a *difference* between two rates, so its
sampling error is the sum of theirs. `MinSynergyTrioGames` (12) is necessarily below it, since a trio's sample is a
subset of its duo's. `MinSynergyBaselineGames` (50) is the one the other two cannot provide — a pairing can clear
its own floor while a baseline is still a coin flip, and a noisy baseline produces a *confidently wrong* number,
not a noisy one. Below any of them the API returns no entry and the real game count, and the UI says which case it
hit — #922.

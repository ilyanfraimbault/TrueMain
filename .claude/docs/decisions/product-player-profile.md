# Player profile

Part of the [decision log](../decisions.md). Format: **Decision** — why — `source`.

**The activity grid has one unit — the UTC day — and the switch picks the window, not the unit.**
Patch draws every day of the current patch, week the last seven, and day the one window narrow enough that
there are no days left to draw and the cells become that day's games. It replaces a four-way switch whose
tabs each changed what a square *meant* (a game, a day, an ISO week, a whole patch), so the control that was
meant to zoom also silently re-labelled the grid. The patch tab was the worst of it: it answered "how did
each patch go" for the signature champion, read from the frozen `champion_aggregate_scopes` (#466), and could
never answer the question the view is for — *which days of this patch did I play*. Two things were given up
knowingly: that per-patch career history is gone from this card (the aggregates cannot be split by day, and
the profile's other panels already carry the career), and with every window now reading live
`match_participants` for all champions there is exactly one source and one scope, so the per-series `source`
/ `scope` / `championId` / `retentionBounded` discriminators came off the payload — they existed to describe
a split that no longer exists. All three windows still ship in **one** request, foldings of one snapshot of
the same rows, which is what keeps two of them from describing the same afternoon differently — #1473, #927.

**The patch window is measured over everyone's matches, never over the profile's own history.**
The schema has no patch calendar: a patch's start date is nowhere stored. So the current patch is the one
whose first tracked game is the most recent (no version parsing, and a straggler ingested for an older patch
cannot fool it), and its span is that first game through its last, over `matches` for the tracked queue.
Bounding it on the *player's* first game of the patch would have cost nothing and been wrong: the days a
player sat out at the start of a patch are exactly the days the grid exists to draw. The group-by is
prod-measured at ~250 ms over the retained window (two patches, ~320k rows) and cached for 15 minutes — one
global fact that turns over once a fortnight, asked once rather than once per page view; the visible
consequence is that for up to that long after a new patch's first game the grid still draws the previous one
— #1473.

**The grid is squares and nothing else: no captions, no legend, no coverage line.**
Fixed 14 px tiles packed from the left, identical in every window. #1473 had stretched them to span the card
and captioned each one with its date, which the product owner rejected on sight: at a fortnight's width a
stretched tile is a fat lozenge, and eleven of them read as a row of buttons rather than as a contribution
grid, whose whole point is that the density of small identical squares *is* the shape of the period. The
three things printed around the grid went with the stretch — the date under every tile, the ramp legend, and
the "which patch · first day – last day" line — because each was either the window's own definition read back
to the reader or a fact the cell's own hover panel already carries. What is left on the card is the window's
total and the tiles — #1479, #1473.

**An idle day is not a lost one, and every day of the window is drawn.**
A day with no games carries `games: 0` and a **null** win rate, and gets its own tile: a 0% day is a
measurement and an idle one is not, so the fill helper returns no colour at all for the second and the
tooltip reads "No games". No day inside a window is ever skipped — the run of idle tiles between two
sessions is what makes a busy stretch legible. Retention (~2 patches) cannot turn that into a lie about an
erased day, because both calendar windows are bounded by matches that are by definition still on disk: seven
days for the week, and for the patch a span read off the patch's own retained rows. Days are UTC, matching
the pipeline's existing UTC-day rule (#907): a viewer-local grid would need a timezone parameter on a public
read or client-side re-bucketing of raw games. Accepted: a late-night game can land on the next day's cell
far from UTC — #1473, #927.

**The player performance panel shows the score and its sample only — no per-component breakdown.**
The #918 panel stacked nine component bars under the average, each with a midpoint tick nobody could read
(the tick marked the model's 0.5 "even lane / average share" baseline, and it looked like a rendering
artefact), plus a subtitle and a footnote explaining the scoring model. It buried the one number a reader
came for. What is left is the average, its verdict on the S→D tier ladder that colours the number itself,
and the four sample figures — each with a one-line hint, since "Top of team 25%" means nothing until you
know it counts games this player outscored their own four teammates. The API still returns `components`:
the breakdown is the natural content of a future drill-down, and the payload is cheap.

## The rank chart is one area with a tier-gradient line, never one area per tier (2026-08-20)

The profile's LP chart used to model each tier as its own chart series — a point carried its rank score under
its current tier's key and left every other tier `undefined` — so that the line *and* the fill colour-shifted
at each promotion. Unovis has no notion of a hole in an area: `getStackedData` turns a missing value into
`0` (`getNumber(...) || 0`). The y-domain floats far above zero (a Challenger's rank score is ~5500), so every
out-of-run zero sat roughly 1300px below the 150px plot, and each tier's area dived through the whole frame to
reach it. Two tier areas then overlapped across the transition and painted a full-height wedge — three
Grandmaster dips of one or two snapshots produced three "triangles" under an otherwise correct line.

What ships instead: **one continuous series** (the rank score), so there is exactly one area path and no
phantom zeros. The tier colour moves onto an **x-axis `linearGradient`** with one stop pair per contiguous
tier run — painted onto *both* the line's stroke and the area's fill, so the whole chart colour-shifts
together rather than the fill staying pinned to whatever tier the player is in right now. The gradient lives
in a zero-sized `<svg>` of our own because the chart's `<defs>` belong to `vue-chrts`; `url(#…)` resolves
document-wide, so it still paints inside the chart's SVG.

**Each run's boundary stop sits on the midpoint with its neighbour, not on its own last/first point** — the
first cut of this shipped with the boundary on the run's own point index, which left a one-index gap between a
run's end stop and the next run's start stop for the gradient to interpolate across, blurring the transition
into a soft blend instead of a sharp cut. Worse, a **single-snapshot run**'s own start and end stops then
coincided (zero width), so it got no flat colour of its own — only the neighbours' blend fading through where
it sat, invisible on a short tier dip. Moving each boundary to `(index ± 0.5) / lastIndex` makes two
consecutive runs share their boundary stop at the *exact same offset* — an SVG hard stop, an instant colour
change with zero interpolated width — and gives a single-snapshot run a real band, extending from the midpoint
with its previous snapshot to the midpoint with its next one. The first and last run instead clamp to 0%/100%
(no neighbour to split a boundary with).

The fill can't reuse `vue-chrts`' own `gradient-stops` prop for its vertical opacity fade — that prop feeds a
gradient hard-coded to one flat colour per category, which is exactly the "pick one colour for the whole area"
limitation this fix removes. The fade is a separate `<mask>` (a vertical white→transparent `linearGradient`,
same 45%→5% opacity numbers the old prop used) layered on top of the tier gradient instead, so hue and
vertical fade vary independently.

Both overrides land via scoped CSS, but on different footing: the line's stroke is a *presentation attribute*
(`.attr('stroke', …)` in `@unovis/ts` `components/line/index.js`), which a plain declaration outranks with no
`!important` needed (same Emotion-label targeting trick as the tooltip override in `main.css`) — while the
area's fill is set via `.style('fill', …)` (`components/area/index.js`), an *inline* style, which only
`!important` beats. The fill selector is tag-qualified to the `<path>` (`path[class*="-area"]`) rather than the
bare class substring — `-area-component` (the wrapping `<g>`) matches too, and letting the rule land there
would apply the mask a second time through inheritance and double-darken the fade.

One guard, line-only: an `objectBoundingBox` gradient needs a non-degenerate box, and a single-tier or
dead-flat history gives the line path zero height — the browser would drop the element entirely. That case
falls back to a flat tier colour. The area's own box never degenerates this way — it spans from the data down
to the scale's zero point, which is never zero-height — so its fill always uses the gradient.

## A Riot ID resolves case-insensitively, in exactly one place (2026-08-26)

Ten services turned a Riot ID into an account row, and they did not agree. Nine compared it with `==` under
Postgres' default case-sensitive collation; the tenth, the champion mains comparison, lowered both halves. So
`Name#tag` answered on `/champions/{id}/mains-comparison` and **404'd** on `/truemains/Name-tag/profile` —
each of the nine carrying a comment claiming "all routes agree on which account a name tag means".

**Case-insensitive is the settled semantics.** A Riot ID reaches us as text a human typed, pasted or
re-typed from a shared link — it is not an identity we issued, the PUUID is. The stored casing still wins on
the way out: the identity a page renders comes from the row, so a profile shows the Riot ID as Riot spells it
whatever the URL said. `/truemains/phantasm-euw1` and `/truemains/Phantasm-EUW1` are now the same page.

All ten callers go through `Api/Services/Truemains/TruemainAccountResolver.cs`, which also owns the tiebreak
that was copied ten times with it: a `(gameName, tagLine)` pair is unique within a routing region but collides
across regions and across renames — which is why that index is deliberately not unique, see `data-aggregation.md` —
so the **most recently active row wins**, `Id` breaking an exact timestamp tie. Locked by
`TruemainAccountResolutionApiIntegrationTests`, which walks several routes per casing.

The lookup is `lower("GameName") = $1 AND lower("TagLine") = $2` rather than `ILIKE`: equality sidesteps LIKE
metacharacters in raw user input entirely, and it is the exact expression a functional index on
`(lower("GameName"), lower("TagLine"))` would serve — which an `ILIKE` could not use. **No such index exists
yet**, so this trades the index seek the nine `==` copies got for a sequential scan of `riot_accounts`. That
was accepted knowingly for this PR: adding one is a schema change (compiled-model regeneration, migration) and
belongs in its own, measured PR.

## The "{player} vs mains" card is gone — one page, one definition of a core build (2026-09-03)

The card (#529) and the build tabs above it read the same aggregate rows and disagreed on screen. Measured on
prod, 튼튼짱짱만욱#튼튼짱짱 on Hwei, 16.17 middle, 7 games over 6 pattern rows: the card's "Core items" showed a
**three**-item path while the Build path and the Build tree right above it showed **two** — same patch, same
lane, same games.

They were two algorithms, not one shared by two callers:

- `ChampionBuildsQueryService` splits the slice into build **variants** keyed by `(first item, keystone)` and
  walks `ChampionBuildPathAnalyzer` *inside one variant*, over a tree pruned at `BuildTreeMinGames = 2`
  absolute support and `BuildTreeMinPickRate = 0.10` of the parent. The dominant variant held 5 of the 7 games;
  every continuation past the second item had a single game and was pruned. Two items.
- `BuildDivergenceAnalyzer.WalkCorePath` pooled **every** variant (all keystones, all first items) and walked
  depth 0→2 gated on a parent-relative `MinPathStepRate = 0.20` and nothing else. Its last step passed at
  exactly 1/5 = 0.20, so the card published a three-item "core build" **backed by one game** — and printed the
  giveaway itself: "14.3% of their games, 100% win over 1 game".

The 20%-only gate was the defect, but fixing it would have left two definitions of "the core build" one scroll
apart, free to drift again on the next threshold change. The card was the weaker of the two — a personal slice
is a median of 3 games (see below), which is thin enough that "how you differ from the mains" is mostly noise —
so it was removed rather than realigned: component, composable, shared types, `PlayerBuildDivergenceQueryService`,
`BuildDivergenceAnalyzer`, the `/divergence` endpoint, its tests and its dev-mock fixture.

**A rebuilt version has to start from the build tabs' own path**, not a second walk of the same rows.

## An empty slice keeps its filters (2026-09-03)

Picking a lane the player never played 404s the player-scoped aggregate, and the page used to hide the whole
header — champion, patch select and lane picker — behind the same `notEnoughData` flag as the notice. The lane
picker renders with `hideAll`, so there was no "all lanes" button to undo the choice either: the only control
left on the page was a link to the *global* champion build. A reader who narrowed to Jungle was, literally,
stuck outside the player's page.

**The controls that produced a state must survive it.** The header now renders over the empty slice, the stat
line reads "no games on this slice" instead of a fabricated `0.0% WR`, the patch select keeps the last patch
the API resolved (it has nothing to read it from while the aggregate is null), and the notice names the active
filters and offers to clear them.

This is the same class of defect as the thin-sample rule in `product-mains.md` (#762): the page degrades, it
does not dead-end.

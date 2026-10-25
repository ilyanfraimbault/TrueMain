# Player profile

Part of the [decision log](../decisions.md). Format: **Decision** — why — `source`.

**The activity grid has one unit — the UTC day — and the switch picks the window, not the unit.**
Day is the one window narrow enough that there are no days left to draw and the cells become that day's
games, week is the last seven days, month the last thirty, and patch every day of the current patch. The
switch is ordered narrowest first, with patch closing the row rather than sorting by width: it is the only
window whose span is not a fixed number of days, so it sits at the end of the calendar ladder instead of
interrupting it between week and month (#1486). Thirty days
and not a calendar month (#1483): every window ends on today and counts back, so a calendar one would open on
days that have not happened yet for the first three weeks of one. It replaces a four-way switch whose
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

**The month window is clamped to the retention floor; the narrower ones need no clamp.**
It is the first window wide enough for retention to bite — `match_participants` is hard-deleted past
`RetainedPatchCount` patches (~2), which is roughly a month — so thirty days back can reach a day where "did
not queue" and "no longer stored" are indistinguishable. Drawing that as an idle tile would be a fabricated
claim about the player, so the window's first day is the later of "thirty days back" and "the oldest game
anyone still has": the grid starts later and says nothing it cannot back. The week and patch windows need
nothing of the sort, being seven days and a span read off matches that are by definition still on disk. The
floor costs no extra query — the cached group-by that measures the current patch already returns a row per
retained patch, so it is the earliest first-game across them — #1483.

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

## The rank chart changes colour where the curve crosses a tier threshold, not at a snapshot (2026-10-20, revises 2026-08-20) — #2174

A rank is crossed at an LP threshold, so the LP curve shows a promotion or a demotion **horizontally**: the
curve is drawn once per tier band and clipped to it (`utils/rank-chart-zones.ts`, `utils/rank-chart-geometry.ts`),
so its line and fill change colour exactly where they cross the band's floor, and each threshold in view is a
dashed line in the tier's colour with the tier's crest sitting on it. The 2026-08-20 version coloured the curve
by each *snapshot's* tier along x (one hard-stop gradient per tier run), which read as a vertical cut at the
snapshot where Riot's answer changed.

- **Below Master the thresholds are fixed** (400 rank-score points per tier, `rankScore`). **Grandmaster and
  Challenger have none:** a region promotes whoever sits above the lowest LP of its Grandmaster / Challenger
  league that day, so those floors are measured — the lowest LP of each league, per platform and UTC day,
  recorded by LadderSync from the apex lists it already reads whole (`apex_cutoff_snapshots`, no extra Riot
  call), served by `GET /ladder/{platformId}/apex-cutoffs`.
- **The days before the first measurement are estimated, not left vertical** (2026-10-21, reverses this
  entry's first "no backfill" call — #2181). Riot keeps no history of the cutoffs, and without one a 90-day apex
  curve still split at every snapshot whose tier changed — almost the whole chart on a GM ↔ Challenger profile,
  which the product owner rejected on preprod. So the first apex refresh that finds no estimate fills every past
  (platform, tier, UTC day) from `rank_snapshots`: the lowest LP among that day's tracked Grandmaster /
  Challenger rows, days under 10 rows left out (`IsEstimated`, once, `ON CONFLICT DO NOTHING` so a measured day
  stays measured, and a measurement replaces an estimate). Our sample is a subset of each league, so an estimate
  errs high, never low — on preprod the day's sample minimum equalled the measured EUW cutoffs exactly. The API
  smooths an estimated day to the median of itself and its neighbours (lower middle at an edge), absorbing a day
  whose sample missed the league's last players.
- **The front never falls back to the vertical split while any cutoff exists.** A snapshot with no cutoff on or
  up to two days before its day takes the history's nearest one, however far, for its band — without drawing a
  line from it. Only an apex history with no cutoff at all (a platform LadderSync does not sweep) still reads the
  band back from the snapshot's own tier, stepping halfway between two snapshots.
- **No figure on the lines and none in the tooltip:** the line says where the tier begins; how many LP that is
  was clutter on a 150 px chart (product owner, 2026-10-20).
- **Still one series, never one area per tier.** Unovis turned a tier's missing points into zeros far below a
  y-domain that floats around 2800+, and each per-tier area dived through the plot to reach them — the
  full-height wedges the first version showed. The curve is one path; only its *clip* changes per band.
- **Own SVG on both hosts.** Clipping per band needs the plot's exact pixels, which an overlay on vue-chrts'
  internal plot area can't rely on; the chart draws itself in plain SVG in the shared layer (the app's own
  stand-in, which drew the same monotone curve, went with it), with the vertical fade as a `<mask>` on each
  clipped copy of the area.

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

## The site's match detail has no Timeline tab; TrueMain stores no win-probability curve (2026-10-12)

**Decision.** The win-probability curve and its turning points are a desktop-app view only. The ingestor computes no
curve and the API sends none; the `match_win_probability` table, its C# port of the model and the site's Timeline tab
are gone. The shared `MatchDetailPanel` still shows the tab when the desktop app hands it the client's timeline.

**Why.** The curve was only ever wanted in the app, where it costs neither a Riot call nor storage: the app shows what
the League client already holds. On the site it stored per-match event data (curve, swings, objectives) that nothing
else reads, for every ingested match, and kept a second implementation of the model in step with the first through a
parity fixture.

**Reverses.** The 2026-10-05 decision (#1911) to compute the curve at ingest and store it one row per match for the
site's match detail.

**Source.** #2009, #1911.

## Known players: one person, several accounts, only what public sources list (2026-10-13)

**Decision.** A known player — a pro, a streamer, a creator, or any notable player — is a *persona* in the code and the
person's own name on the site, never a generic noun: `/players/{slug}` lists their public links and the Riot accounts
public sources (Leaguepedia, LoLPros, …) attribute to them that TrueMain tracks, each leading to the regular profile —
an untracked account has no page to open, so it is not shown (2026-10-14, #2018), and the list opens on the account
played most recently. On `/truemains` a
linked account's row shows the person's name, which links to that page, while the rest of the row still opens the
account's profile; the profile banner links back with "{name} · all accounts". The page sits outside `/truemains`
because a known player need not be a true main of anything.

Accounts are linked only when a public source lists them, and every link keeps its source. TrueMain infers nothing — no
smurf detection from play patterns, no historic Riot IDs: Riot's policy forbids de-anonymising a player who cannot
reasonably be identified from visible information. The intake is additive and never moves an account another persona
holds (a conflict is reported for an operator to settle), and untracked accounts are seeded only on platforms the
pipeline ingests (revised 2026-10-19, #2134 — it was "platforms the site shows").

**Why.** Visitors reach a famous player through one of their accounts and had no way to know whose it was or find the
others. The first `personas` table (dropped in #1244) was half-built with no consumer; this one ships with its reader.

**Untracked platforms: stored, never seeded (2026-10-19, #2134).** An account a source lists on a platform outside the
ingested set (`EUW1`, `KR`, `NA1` today) is stored as published and answered `untracked`, but never queued through the
seed intake — whatever `seedMissing` says. Match ingestion only claims candidates on its configured platforms, so a seed
on `BR1` or `TR1` spent three Riot calls on an account that was never ingested, and the platforms scrapers guess for
such accounts mostly ended on a summoner-v4 404 (#2133). The row still links on its own if that platform is ever
ingested. The batch reports those accounts in one log line, and placeholder names a source serialises for a missing
value (`undefined`, `null`) are rejected as `invalid`.

**Conflicts are the operator's, and decisions outlive the scrapers (2026-10-23, #2253).** Every claim on an account
another persona holds is recorded, and the admin's Known players page settles it: move the account, keep it, detach it
from both, or merge the two personas. The intake still never decides on its own — a source's weekly listing is not
evidence against another source's. A decision is written where the next run cannot undo it: the persona that lost the
account gets an exclusion (its claim is answered `excluded`), and a merged persona stays as an empty row pointing at the
kept one rather than being deleted, so its slug keeps redirecting (301 on the web page), the intake folds its source's
entry into the kept persona instead of recreating it, and the conflict history keeps both names.

**The operator's edits hold the same way (2026-10-24, #2019).** The admin adds a known player by hand (source
`manual`), renames one, adds or unlinks an account, adds or removes a link, and removes a known player. Each decision is
written where the weekly batch cannot undo it: an unlinked account is an exclusion for that persona, a removed link a
link exclusion the intake skips, a name the operator set is locked against the sources' names, and adding an account
or link back by hand lifts the persona's own exclusion of it. Removal keeps the row and slug, like a merge, but empties
it: the page answers 404, and the intake answers `removed` and writes nothing, so a source that keeps listing the person
cannot bring the page back. Two kinds, because they answer two different requests: a plain **removal** is for a persona
that should not exist (its accounts stay free for whoever a source lists them under); a **takedown** is for a person who
asked not to be listed, and bars their accounts from every persona, since another source may list them under another
slug. A manual account still goes through the intake's rules: another persona's account opens a conflict rather than
moving, and a takedown wins over a manual add.

**Same name and one account is one person; same name alone is the operator's call (2026-10-25, #2271).** Most
conflicts opposed two personas with the same name that two sources published under two slugs (`reksaiking` from
DeepLoL, `reksaiking-reksaiking` from dpm.lol). The operator's rule:
two known players with the same name — lowercased, spaces, dots, dashes and underscores dropped — that claim the same
Riot account are one person, so that conflict is merged without asking, into the older persona (the slug the site
advertised first), through the same merge an operator would pick and audited with the actor `auto`. The intake sweeps
after every batch that raised a conflict, and the API sweeps once at startup (#2273) — the scrapers run weekly, so
without it a deploy left the backlog queued for a week; the admin's button sweeps on demand. A same name **without** a common
account is not enough — Leaguepedia lists several pros called Rain — so those personas go to a homonyms queue where the operator merges a selection or marks them as different people.
The mark is per pair (`persona_distinct_pairs`), so a group only comes back when a new persona with that name appears,
and a merge carries the merged persona's marks over to the kept one.

**Source.** #2016, #2134, #2253, #2019, #2271, #2273.

**A main champion row reads its lane and play rate only** (2026-10-13). #1498 had put `80 of last 200` under
each main's name, so the count would name its denominator next to the champion page's per-patch count. On the
card it read as clutter: the play rate on the right already says the share of the recent window, and the
sample line repeated it in another form. The line is gone; the app's dashboard still fills the row's `meta`
slot with its own figures (`DashboardChampionsCard`). A retired main keeps its warning dot.

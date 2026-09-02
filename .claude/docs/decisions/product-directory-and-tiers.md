# Champion directory, tier list and served patch

Part of the [decision log](../decisions.md). Format: **Decision** — why — `source`.

**Tier score is presence-first (pick rate + ban rate, weighted above a sample-shrunk win rate) — this reverses #920's "bans don't feed the tier score".**
The old blend (85% raw win rate / 15% patch-max-normalized pick rate) let a handful of games at a flattering win
rate fluke into S-tier ahead of a heavily-played, average-winrate staple — the min-sample floor
(`ChampionsList:MinSampleGames`) only kept out the worst 1-2 game flukes, not the noisy band just above it. Two
changes together fix it (#971): win rate is bayesian-shrunk toward the field's prior in proportion to sample size
(`wrAdj = (wins + K·prior) / (games + K)`, `K` = `ChampionTier:WinRateShrinkageGames`) *before* scoring — the
actual fix for micro-sample flukes, since weighting alone can't offset a raw 70%-vs-53% gap; and the blend itself
is re-weighted to pick rate (45%) + ban rate (30%) + shrunk win rate (25%), because pick/ban share are
population-scale signals a single game barely moves, while a raw win rate is exactly the number a handful of
games swings hardest. All three metrics are percentile-ranked *within the same lane* (not min-max against a
single patch-wide maximum, and not patch-wide at all) before weighting — this also fixes a lane-size bias:
UTILITY has far fewer playable champions than MIDDLE, so a support's raw pick rate is mechanically higher for the
same "share of the meta", and the two weren't comparable un-normalized. On a patch with no ban data at all
(pre-#920), the ban term is dropped and its weight folds back into pick rate and win rate proportionally — never
a fabricated 0%, and tiers stay comparable across ban-data and no-ban-data patches — `ChampionTierCalculator`.

**The static champion list drops Data Dragon entries with an id at or above 10 000 — alternate-mode kits, not champions.**
Patch 16.15 ("League classique") added 60 legacy kits to `champion.json`: alias `Jade_<BaseAlias>`, key
`60000 + <base key>`, and the *same display name* as the original — `Jade_Ahri` (60103) sits next to Ahri
(103). The ingestor only aggregates queue 420, so those ids never carry a single stat: unfiltered they
doubled every search hit and picker row with a dead end and put 60 empty pages in the sitemap. The floor is
`isLiveChampionId` in each app's `shared/utils/ddragon.ts`, applied in the one endpoint both apps read
(`server/api/static/champions.get.ts`). 10 000 rather than 60 000: the highest real Riot key is 950, so the
cut keeps an order of magnitude of headroom while catching a future mode built the same way — #966.

**A champion gets at most two lines in the directory — its dominant lanes — and the cap is applied before tiering.**
`champion_aggregate_scopes` holds a row per `(champion, lane)` the population played, and champions flex, so the
directory was up to 5 × N: 561 lines for 173 champions on patch 16.15. That list answers "which champion-lane
pairs exist" when the question asked is "which champions are strong". The lines past each champion's top two
carried 5.9% of the games between them, so the cap costs almost no evidence: `ChampionDominantLaneFilter` keeps
the two most-played lanes (`ChampionsList:MaxLanesPerChampion`), and a *secondary* lane only if it holds 10% of
the champion's own games (`MinSecondaryLanePlayRate`) — 37 of the 152 champions with a second lane play it under
5% of the time, which is an off-role pick, not a second identity. The most-played lane is kept whatever its
share, so an evenly-spread five-lane flex still appears once rather than vanishing from a list of champions.
**Before tiering, after the sample floor**: a tier is a percentile *within a lane*, so an off-role line must not
be one of that lane's peers, while a line that never cleared the sample floor must not consume one of the two
slots. One filter in `ChampionSummariesQueryService` covers the directory, the tier list and the homepage teaser,
because all three read that one cached payload — #1082.

**A tier-list chip is a portrait and its lane badge — the name and the three rates are tooltip content.**
The chip used to be a pill: icon, name, lane glyph and a `52% WR · 12% PR · 8% BR` line, ~190 px wide. Five
tier groups of those is a page you scroll rather than scan, and the question the tier list answers ("who is
strong right now") is answered by the *faces* — a player recognises a portrait faster than they read a name.
So the chip is now the portrait alone with the lane badged into its bottom-right corner (the anchoring the
directory already uses for the secondary rune tree), and hovering gives the name plus `Win rate / Pick rate /
Ban rate`. In the tooltip the values *are* coloured on the `--color-data-*` axis, which the directory row
deliberately refuses — colour that is noise in a dense table of forty rows is the whole point of a panel
showing three numbers. Only the win rate uses the good↔bad axis; pick and ban rate fade to muted at the low
end instead of turning amber, because a pocket pick is rare, not bad — and they get *separate* bands, since
they do not share a denominator: on 16.15 the median pick rate is 0.3% against a median ban rate of 2.6%, so
one threshold pair would have coloured every ban and no pick (`app/utils/rate-tone.ts`). The cost is
that no tooltip opens on touch, so the link's `aria-label` carries name, lane and all three rates — that
string is also what a screen reader gets, and it is the reason the missing ban rate is dropped from it
entirely rather than announced as "dash BR".

## A patch is served only once it can fill a directory (2026-08-12)

**#1109.** The public reads used to default to the newest patch holding *any* aggregate row, and the directory
then dropped every `(champion, lane)` line under `ChampionsList:MinSampleGames`. The two rules contradicted
each other for the whole window between a patch's first fold and its first few thousand games. On production,
hours after 16.16 shipped, seven one-game lines moved the entire site onto it — an empty directory, an empty
tier list and a "4 main games analyzed" hero, while 16.15 sat beside them with 331 628 games. It self-healed
in hours and recurred **every patch day**.

- **The bar is counted in lines past the floor, not in games.** `ChampionsList:MinServablePatchLines` (50) is
  measured on exactly what the directory renders, so it can't clear while the page stays empty. A raw games
  threshold is a proxy for that and would have to be re-tuned as the site's volume grows; lines past the floor
  are self-describing at any volume. 50 ≈ 40 champions, roughly three to five hours into a patch at current
  volume, against the ~560 lines a settled patch reaches.
- **The definition is shared, not re-implemented** (`Data/Aggregation/ChampionDirectoryLines.cs`). The count
  that gates serving, the count the directory renders and the count the admin patch-coverage page reports are
  one fold. Two copies would eventually disagree, and the failure mode is specific: the page would certify a
  patch the site had refused, or bless one it had switched onto.
- **The fallback is reported, never silent.** The served patch travels in `patchVersion` and drives the patch
  picker, so the site says "16.15" while it is showing 16.15, and the thin patch stays explicitly selectable.
  Serving old data under a new patch's name would be strictly worse than the empty page it replaced.
- **Nothing clears the bar ⇒ serve the newest anyway.** On a fresh deployment a thin directory is the honest
  state and an empty one is not. Same branch makes `0` the documented off-switch.
- ~~**The homepage volume chips span two patches**~~ (`ChampionsList:HomepagePatchWindow`) ~~and name the range
  they cover.~~ **Superseded 2026-08-16** — the chips are lifetime totals now and the window is gone; see the
  next entry. What survives from this bullet is the asymmetry it exists to protect: the tier-list teaser does
  *not* merge patches, because a tier is a percentile within one patch's field and a blended ranking would
  describe a meta that never existed. That is still why the teaser and the volume figure read from different
  calls.

Player- and champion-scoped views were never affected: `ChampionScopeLoader` has had
`ResolveLatestPatchAboveFloor` since long before this, for the same reason. #1109 is that idea finally applied
to the global reads.

Shares a trigger with #1107 (CommunityDragon's unpublished patch branch aborting the folds) but nothing else —
and fixing #1107 makes this one *more* urgent, since the folds now succeed on patch day and the flip onto a
thin patch would happen sooner.

## The homepage hero counts a lifetime, compactly, and never names a patch (2026-08-16)

Reverses the chip half of #1109 (above). The patch-window figure was correct and unreadable: `173 champions
ranked over 16.16–16.15` beside `490,365 main games analyzed over 16.16–16.15` asked a first-time visitor to
hold a data-scoping caveat before they had any reason to care, and the qualifier itself was only there because
the number underneath it was patch-scoped and would otherwise have overstated its reach.

- **The volume figure is now every game the aggregate table holds**, all patches (`GetTotalGamesAsync` — one
  `SUM` over `champion_aggregate_scopes` on the tracked queue, cached **30 min** — the table only grows and no
  index leads with `queue_id`, so this is a full scan whose cost rises with the site's age, while the chip's
  three significant digits move about twice an hour). With nothing patch-scoped left to disclose, the
  qualifier goes, and so does the failure it was compensating for: a lifetime total cannot crater on patch day.
  On production the aggregates *are* the frozen history — `MatchDataRetention:AggregateRetainedPatchCount` is
  0 there — so this is a genuine all-time count; preprod opts into aggregate retention and will read lower,
  which is correct for what preprod holds.
- **`ChampionsList:HomepagePatchWindow` is deleted**, with `GetServedPatchVolumesAsync` and
  `ChampionPatchVolume`. The servable-patch bar (`MinServablePatchLines`, the other and larger half of #1109)
  is untouched and still reads lines past the floor through the same shared fold; only the per-patch *volume*
  counters nobody reads any more are gone.
- **"N champions ranked" was dropped rather than kept unqualified.** Of the three chips it is the one that is
  inherently patch-scoped — a champion is ranked *on a patch* — so keeping it would have dragged the
  qualifier back onto the row it was being removed from. Two chips that mean the same kind of thing beat three
  that don't.
- **Counts are rounded down, not to nearest** (`formatCompactCount`): `490k`, `41k`, `1.2M`. Exact figures
  invited the reader to treat a half-hour-cached number as a live counter, and a site whose pitch is honest
  sample sizes should not be the site that rounds 999,600 up to `1M`. One decimal below 10 (`4.1k`), none
  above (`490k`), where it is noise.

# TrueMain — decision log

Settled product and architecture decisions **with their rationale**, so a session doesn't re-litigate them or
propose something a past incident already ruled out. Companion file: [features.md](features.md) (the *what*).

Format: **Decision** — why — `source`.

Last verified against `develop` on 2026-07-28.

---

## Product

**Stats are computed from *true mains* only, not from every game on a champion.**
Averaging all games drowns the specialist signal. A player is promoted to "true main" through a
games-vs-mastery investment signal. This is the thesis of the site — `README.md`.

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

**Champion timeline-leads ("Lead vs role opponent") was removed; matchups stayed.**
Not worth its maintenance cost; effort went to power spikes instead. `JobMode.MatchupLeadAggregationOnly` and
the `MatchupLeadAggregation` options section were deliberately **not renamed** — they are config-facing
(`INGESTOR_JOB_MODE`) and renaming risks a prod startup failure for no functional gain — #889.

**Power spikes are per-core-build bars anchored on events, not a time curve; bar height is excess acceleration.**
A blended cross-build answer is wrong (Botrk vs Kraken rushes behave differently). Winrate delta was
considered and rejected as confounded — completing a third item correlates with already winning — #890, #775.

**Scoping the aggregate to a build scopes the games, not the items — the item set has to be intersected at
read time** (#1021). The event rows carry `(BuildFirstItemId, BuildKeystoneId)`, but within that slice *every*
item a game completed produces a row, so a situational item bought in a minority of the slice's games sat in
the table beside the build's own. Ranking the bars by magnitude then let it outrank a core item, and the panel
showed items absent from the tab's core path. Fixed on the read: item events are intersected with the core
path (`ChampionCoreBuildPathResolver`, resolving it exactly as the builds read does) and returned in **build
order**, not by magnitude and not by mean minute. Two consequences worth keeping in mind. **The panel is
withheld rather than approximated** when no path resolves — the aggregate slice is gone, say nothing, because
"which items are this build's" is the one question that could not be answered. And **the bar row is not a
timeline**: each item's minute is a mean over its own games (those where it was completed at all), so two
adjacent bars can sit a minute apart while describing disjoint cohorts. That is what made the row read as
impossible; ordering by the build rather than by those minutes stops presenting them as a sequence, and
conditioning them on the preceding core items is #1022.

**"Completed item" is the build path's eligibility rule, shared, not a local restatement of it** (#1021). The
powerspike fold tested `IsFinalItem && !IsBootsItem`, but `IsFinalItem` only means "nothing builds out of
this" — equally true of potions, control wards, trinkets, Doran's and support-quest items, all of which were
being folded and rendered as power spikes. `FinalBuildResolver.IsEligibleFinalBuildItem` is now public and is
the single definition; an item that cannot appear in a build cannot be that build's power spike. Ids are
mapped through `GetDisplayedBuildItemId` for the same reason the build path does it, or a transform item
would be named one way by the fold and another by the dim tables and never match.

**Matchup-scoped power spikes are a dimension on the aggregate, not a live recompute — because a live
recompute is impossible.**
The other matchup-filtered sections fold `match_participants` live (#923), so #957 was written assuming the
same. It cannot work: a spike is the second difference of the power curve on a ±3-minute window around an
arbitrary event minute, and retention prunes the dense per-minute grid to {5, 10, 15, 20, 30} the moment the
match is folded (#772). The window has nothing left to sit on. What made it cheap instead is that the spike is
*already* opponent-relative — the fold resolves the lane opponent to build the diff series and was simply
discarding their id. Recording it splits the grain exactly: every game belongs to one opponent, so the
unscoped read recovers its old numbers by summing across them, which is what it already did.
Two consequences, both deliberate. **Coverage is forward-only**: pre-#957 rows sit at `OpponentChampionId = 0`
and no filter can match them, so a matchup's spikes start empty and fill in patch by patch — the section says
so rather than blaming the matchup. And **retention rolls the split back up** when a patch leaves the live
window: without that, a 500-game build shredded into 40 opponent rows of ~12 would fall entirely under the
`PowerspikeEventMinGames` floor and the next cycle would delete the patch's spikes from the *unscoped* read
too. The baseline curve stays champion-wide on purpose — it corrects the global concavity of lead curves, and
recomputing it on a 4-game matchup would swap that correction for noise and subtract the signal from itself — #957.

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

**Share cards degrade in three steps and never print a number the API did not return.**
An OG image is read as a screenshot of the page, so a filler `0%` is indistinguishable from a
measurement — the "no fabricated numbers" rule that governs the pages binds harder here, because the
reader has no page to check it against. Champion card: full stats when a `GET /champions` row exists →
portrait + name and *no numbers at all* when it doesn't → plain branded card when even DDragon is
unreachable. A null `banRate` (a patch before #920's ingestion) drops that one tile rather than zeroing
it, matching the em dash the directory prints. Truemain card: a missing ranked snapshot reads
"Unranked" (a real answer, unlike 0 LP), absent `wins`/`losses` drop the record line *and* the win-rate
tile, no classified main drops the champion block, and an unknown player falls all the way back to the
branded card — the page renders "not found" for the same input, so a profile-shaped preview would be a
lie. The dedication score is only printed when its `championId` matches the main shown beside it — #926.

**Share cards resolve their own data server-side instead of receiving it from the page.**
`nuxt-og-image` encodes the template props into the (signed) image URL, which is minted during SSR — but
both pages fetch `server: false` (the #149 hydration fix on champions, the deliberate no-cross-viewer-SSR
rule on profiles), so at that moment the page holds no numbers to hand over. The URL therefore carries
*identifiers only* and the templates resolve the real slice through `server/api/og/**` when a crawler
renders the image. The alternative — adding an SSR-enabled fetch to the pages purely to feed the card —
would put a backend round-trip on every human page view to serve the unfurl path. Accepted consequence:
the card's numbers are resolved at share time, so they can differ from a page the visitor left open —
both are real, an hour apart at most (the cache TTL) — #926.

**The champion page server-renders its build as prose — the one SSR round-trip the page pays.**
#926 declined an SSR-enabled fetch on this page because it would put a backend round-trip on every human
page view to serve the unfurl path. #1123 takes the same cost for a different benefit and reaches the
opposite answer, so the difference is worth stating rather than reading as a reversal. Measured on
preprod, `/champions/{id}` shipped **~1.5 kB of visible HTML and zero build content** before JS — no rune
name, no item name, not the word "Runes" — under a `<title>` promising "Ahri Build". A title that promises
a build over a page that delivers a shell is thin content, and it is why the champion pages could not rank
for their own subject. That is a permanent, sitewide ceiling; a share card is one unfurl.
Three things make the cost small enough to accept. The fan-out sits behind a 1 h `defineCachedFunction`
keyed on (champion, lane, patch, rank), so it is **one backend call per slice per hour, not one per view**.
The endpoint resolves the ids to *names* server-side and returns ~1 kB, so the client never receives the
~373 KiB item map that made "just SSR the existing fetches" impossible in the first place. And it is keyed
on the **URL** filters rather than the reconciled `selectedPatch`/`selectedPosition`, which would flip the
key once the client-only aggregate lands and cost a second round-trip on every load.
Not a #149 regression, and the distinction is the load-bearing one: #149 was a *client-only* fetch racing
SSR and winning, so the server rendered content the client's first render didn't have. This fetch is
SSR-enabled and travels in the Nuxt payload, so hydration reads the same object the server rendered from —
the two agree by construction. Every interactive panel stays `server: false` exactly as before.
Accepted consequences: the summary is at most an hour behind the panels above it (same TTL as the share
card, same reasoning), and it describes `builds[0]` — the tab the page opens on — never "the best build",
which would describe something the reader isn't looking at. It is **visible**, never `sr-only`: text
written for a crawler and hidden from the reader is cloaking — #1123.

**"Client-only fetch" has to be enforced on the *side*, not merely intended — an immediate watcher is not client-only.**
`useTruemainFetch` (profile / rank-history / matches) is hand-rolled refs precisely because these payloads are
per-viewer and must never enter shared SSR HTML, but its initial run hung off `watch(..., { immediate: true })`,
which fires during SSR too. It then *won* the race: the page render is meanwhile awaiting its two SSR-enabled
static lookups (rune tree + summoner spells, external DDragon/CDragon calls), so a local
`/api/truemains/{tag}/*` hit resolved first and the profile landed in the server-rendered markup — while the
client's first, hydration render always starts in the loading state. Vue reconciled skeletons against
rendered content, hit `insertBefore: node is not a child of this node`, then crashed on a null component in
the patch loop, and `/truemains/{nameTag}` sat in its skeletons **permanently** on any full page load
(client-side navigation was fine — it hydrates nothing). The initial run is now `onMounted`, which cannot run
on the server. Consequences worth knowing: the SSR markup for a profile is *always* the skeleton branch, and
the SSR `<title>` is the raw `Name-TAG` slug rather than `Name#TAG` — both are the price of the
no-cross-viewer-SSR rule, not oversights, and "fixing" either by SSR-ing the profile reintroduces the hang.
Disabling SSR on the route or timing out the fetch were both rejected: neither addresses the mismatch, and
the route is a primary, indexable one — #862.

**The activity grid's four modes read two different tables, and the response says so rather than reconciling them.**
`match_participants` carries a game's date but is hard-deleted past `RetainedPatchCount` (~2 patches);
`champion_aggregate_scopes` is frozen forever (#466) but its grain is (account, champion, patch). So the
game / day / week modes physically cannot show last season and the patch mode physically cannot show a day —
the modes are not four views of one dataset. Every series therefore ships its own `source`, `scope`,
`retentionBounded` and coverage range, and the patch series is champion-scoped to the signature champion.
Originally the UI printed a standing coverage line built from those fields; #959 replaced it with a per-cell
hover tooltip and dropped the line, on the reasoning that the grid itself (an empty period clamped to where
the data stops, see the next entry) already carries the retention story visually, and a permanent paragraph
under a hover surface was reading as clutter rather than as a needed disclosure. The payload fields are
unchanged and still drive the day/week window clamp below — only the standing prose describing `source`/
`scope` was cut. Three options were rejected for the underlying multi-table split: scoping *every* mode to one
champion (throws away the "how much did you play" answer the grid is for), splitting the aggregate by day
(it has no day), and quietly labelling all four "activity" (that is the silent disagreement the metadata
exists to prevent). Two consequences kept on purpose: the patch total is a *different population* from the
day total over the same patch, and all four granularities ship in **one** request — three of them are
foldings of the same rows, so one snapshot is both cheaper and the only way two modes cannot describe two
different afternoons — #927.

**An erased period is not an idle one: the calendar grids stop where the data stops.**
The day / week series clamp their window to the oldest game still on disk and emit **no cell** before it,
rather than drawing 30 empty squares over a month retention deleted. Inside the covered range an empty cell
*is* emitted, with `games: 0` and a **null** win rate — a 0% period is a measurement and an idle one is not,
so the fill helper returns no colour at all for the second and the tooltip reads "No games". A player whose
whole retained window has been pruned gets three empty series and a populated patch series, with copy pointing
at it. Days and weeks are UTC (Monday 00:00 for weeks), matching the pipeline's existing UTC-day rule (#907):
a viewer-local grid would need a timezone parameter on a public read or client-side re-bucketing of raw games.
Accepted: a late-night game can land on the next day's cell far from UTC — #927.

**Patch mode is wired to the dedication card's own numbers, not to a parallel query.**
It resolves the signature champion through `MainDedication` — the single place that decides what one is — and
groups the exact scope rows that helper's career lateral sums (account + champion + ranked queue, no platform /
position / bracket narrowing). That makes `patch.games == dedication.careerGames` and
`patch.buckets.length == dedication.patchSpan` invariants a reader can check by eye, since the two panels sit
centimetres apart. A narrower filter (per platform, per lane) would have been defensible on its own and would
have made the grid disagree with the card above it — #927.

**The matchup tool judges the lane over its own sampled games — this finishes #1111's merge.**
#1111 put the recommendation's figures and the matchup's lane figures on one line but left the lane half reading
`champion_matchup_stats`. Two populations behind one strip, and they disagree in a way readers spot immediately:
the aggregate's champion side is **mains-only** since #1087 while a composition sample takes any pilot, so a
matchup showing **"8 games used · 0 by mains"** sat beside a lane win rate of **"—"**. Reported from production
as "pourquoi il n'y a pas de lane winrate ? pourtant tous les matchs qu'on a récupéré ont le bon matchup" — and
that is exactly right: those games *are* games of the matchup, they were simply not the games being measured.
The backend now judges the lane over the selection itself, so every cell counts the same games and the strip
makes one request instead of two.
↳ **Not the scan #606 retired.** The selection already holds its games' `(MatchId, ParticipantId)` keys — at most
`CompositionSearch:TopK` (100) of them — so this reads two snapshot rows per game by key. #606 retired a
self-join over every retained match, which is a different thing.
↳ **The trade accepted:** the lane now rests on tens of games instead of the aggregate's hundreds, so it is
noisier. It is also *the right games*, the cell always prints its own denominator ("of 5 decided"), and
`MinDecidedLaneGames` is deliberately **not** applied here — that floor belongs to the champion page's
leaderboard, where a reader is comparing matchups; here they are looking at one draft, and blanking the figure
would fail exactly the thin drafts the tool exists to answer.
↳ **"The lane was won" is now defined once**, in `Core/Lol/Lane/LaneOutcomeRules.cs`, shared by the ingestor's
fold and this live pass. The threshold stays a separate option per consumer — changing the ingestor's re-defines
every *stored* counter and cannot be applied retroactively (#919), while this one recomputes per request — but
both default to the same constant, and a deployment overriding one must override both — #1117.

**`/matchup` carries one line of numbers, not two — and it stores the XP gap beside the gold one.**
The page had grown a "This matchup" strip (games / win rate / matchup rate / lane WR / gold @15) above a
recommendation card that opened with its own (games used / draft match / win rate): **two `games` figures and two
`win rate` figures centimetres apart**, measuring different populations, with nothing on screen saying so. Merged
into four cells — games used, draft match, win rate, lane win rate — on the reasoning that every game in the
sample *is* a game of this matchup. **The win rate kept is the sample's**, decided by the product owner; the
matchup-wide record (its own game count, win rate and matchup rate) is gone from the page rather than shown
twice, and each surviving cell states its own denominator because the two populations are still different sizes.
↳ The strip stays mounted **outside** `RecommendationPanel`, which is #1098's reason and still holds: that card
does not render on the standard-build fallback, and the figures used to vanish exactly where the reader most
needs them. The provenance-drawer *button* moved into the strip while the drawer itself stayed in the card, which
already fetches the item / rune / spell maps it needs — the page owns the open flag between them.
↳ **XP @15 is stored, not derived** (`LaneXpDiffSum` over its own `LaneXpDiffGames`, mirroring #976's gold pair).
Gold is who bought more, XP is who is bigger, and they routinely disagree: a lane won on kills and lost on waves
shows a gold lead over an XP deficit, which the next all-in reverses. Only gold is banded — 300 XP is a third of
a level, not "a very good lane" — so the verdict badge stays gold's alone and the two gaps share an uncoloured
line, since one tone cannot speak for two numbers pointing opposite ways.
↳ **The migration re-folds rather than backfilling**, because an additive flag-gated fold cannot correct itself.
It was nearly free to do here: #1087's own wipe had not yet reached production, so the two migrations apply back
to back and prod re-folds its window **once**, with gold and XP together. Preprod, which had already consumed
#1087, paid a second re-fold of its single retained patch — #1111.

**Measurements are set in Inter again: the mono stat face is withdrawn.**
#1060 lifted the `--font-mono` → Inter alias and put `stat-value` / `stat-label` on Geist Mono, on the argument
that a technical face gives numbers presence and that the value/label pair needs two registers. Withdrawn by the
product owner in #1111: across a dense page it read as a second, unrelated typeface rather than as a register.
The pair keeps its separation from size, weight, casing and tracking — which was always doing most of the work —
and `tabular-nums` still aligns the columns. Geist Mono stays loaded for the few places monospace is the *meaning*
rather than a flourish: tier letters, the empty-slot glyph, hex codes on `/dev/design-system`. One edit in
`main.css` reaches every stat on the site, which is why the family lives in the utility and not at the call
sites — #1111.

**The matchup opponent-search reads the aggregate, like the panel — this reverses #606's "the search stays live".**
#606 kept the `opponent=` path on a live self-join because "an aggregate built at floor 10" could not answer a
one-game lookup. The rows were never stored with a floor, only the read applied one, so the premise was wrong from
the start — and the split cost real correctness. The live join sees the retention window (2 patches of
`match_participants`) while the aggregate keeps every patch it ever folded, so on production the same matchup
answered **22 games / 27% on the leaderboard and 13 games / 15% in the search**. Worse, #976 had already moved the
search's *lane* counters onto the aggregate while leaving its games live, so rows came back reporting a gold gap
averaged over **more lanes than the games shown beside them** (Singed: 13 games, 16 lanes). Both halves now come
from the same rows, and the search keeps its floor-free contract by simply not applying the read's floors. The
player-scoped slice stays live — the aggregate has no account dimension — #1087.

**The matchups leaderboard floors on a *share* of the champion's games, and ranks on Wilson bounds, not the raw rate.**
An absolute floor cannot work across champions three orders of magnitude apart in volume: 10 games is the whole
matchup on a rarely played champion and 0.07% of the sample on a heavily played one. Measured on Viego JUNGLE, the
11 opponents under 30 games were **0.3% of 53 739 games and held 5/5 of "best" and 3/5 of "worst"** — the panel was
a small-sample detector, because on a field of 86 opponents the most extreme rate is essentially always the
thinnest sample. Two changes, both needed: `MinMatchupPlayRate` (0.5%) combined with `MinMatchupGames` by taking
the **larger**, which keeps 51 of 71 opponents (94.6% of games) on a popular champion and goes inert on a thin one;
and ranking best on the Wilson **lower** bound and worst on the **upper** one, which self-regulates by sample size
so no floor has to be tuned per champion. Three of the five previous "best" matchups had an interval containing the
50% baseline. The floors are the read side's, so moving them is a config change and never a re-fold — #1087.

**The lane win rate carries its own floor, because it is its own sample.**
`MinDecidedLaneGames` (10) is separate from every games floor above it: the production median decided/games ratio
is **0.58**, so a row clearing 40 games can rest its lane column on six decided lanes — and it was printing "100%
lane" off seven, the most confident-looking cell on the panel resting on its smallest sample. Below the floor the
rate is null (an em dash) while `decidedLaneGames` is still returned, so the caller can say *why* rather than
silently omitting it — #1087.

**The matchup folds count mains of the champion, not every account we know.**
`champion_matchup_stats` gated on `RiotAccountId != null` while the champion aggregates feeding the header, the
tier list, the trend and the builds gate on `main_champion_stats.IsMain`. On production that put **14 576 games
behind the matchups panel and 4 605 behind the header directly above it** — same champion, lane and patch, ×3.2 —
and the read-side comment asserted the two cohorts matched. The gate now lives in `Data/Aggregation/MatchupCohort.cs`
so the two folds that write those rows cannot drift apart from each other or from the pattern reader. **Champion
side only**: the opponent stays whoever held that lane, since narrowing both sides would measure mains-versus-mains,
a different and far thinner question. Because both folds are additive and flag-gated, tightening the gate corrects
nothing already written — the migration wipes the table and re-folds the retained window, which loses the matchups
of patches whose raw matches are already gone (accepted: the panel became per-patch in the same change, so those
patches were no longer readable anyway) — #1087.

**The matchups panel follows the page's patch filter on the global route, and deliberately does not on the player one.**
It forwarded position and elo but never patch, and its aggregate outlives the matches it was folded from, so the
panel spanned **16.12→16.15 (53 739 games) under a header reading 4 603** — two contradicting numbers a few
centimetres apart. The player-scoped slice stays cross-patch on purpose, for the opposite reason the global one
needed scoping: one player meets the same lane opponent a handful of times *in total*, so a patch filter would put
nearly every opponent under the 3-game per-player floor and empty the panel — #1087.

**The draft tool is the "Matchup" page (`/matchup`), and its opponent is the *role* opponent.**
"Lane opponent" is meaningless for a jungler, and the page is not a build editor — it answers "what do I build
into this opponent". The wording is now `role opponent` everywhere in that feature, down to
`CompositionSearchOptions.RoleOpponentWeight`. `/builder` stays alive as a query-preserving redirect (the three
matchup inputs are deep-linked, so shared links exist) and is excluded from the sitemap. The *laning-phase*
vocabulary elsewhere — performance score, power spikes, champion matchups — genuinely means the lane and was
left alone — #939.

**The recommendation shows no situational-items row.**
#921 rendered the API's `situationalItems` (off-core items with pick/win rates) under the build panel. It
restated what the build tree above it already shows, minus the ordering, so #939 removed the whole chain —
component, read-model field, backend aggregation and the `SituationalItemCount` option. Reviving it means
reviving the aggregator, not just the component — #939.

**The player performance panel shows the score and its sample only — no per-component breakdown.**
The #918 panel stacked nine component bars under the average, each with a midpoint tick nobody could read
(the tick marked the model's 0.5 "even lane / average share" baseline, and it looked like a rendering
artefact), plus a subtitle and a footnote explaining the scoring model. It buried the one number a reader
came for. What is left is the average, its verdict on the S→D tier ladder that colours the number itself,
and the four sample figures — each with a one-line hint, since "Top of team 25%" means nothing until you
know it counts games this player outscored their own four teammates. The API still returns `components`:
the breakdown is the natural content of a future drill-down, and the payload is cheap.

**Roaming is a badge in the header, not a panel.** The #536 panel gave a full below-the-fold section — title,
subtitle, sample line, three hand-drawn bars and a verdict word — to three cumulative averages, and two of the
three states it could reach ("Balanced", "Lane-focused") were "this champion is like most champions", which is
not worth a section. Nobody visits a champion page to learn it kills people out of lane before minute 15. What
is left is one `Roamer` badge next to the win rate, shown only when the @15 average clears `ROAMER_KP15` (1.5,
`web/app/utils/roam-verdict.ts`) — the threshold is a product call, so it lives in the frontend like
`laneVerdict`'s bands — with the number itself in the tooltip. The badge is deliberately one-sided: not being
a roamer is the default the rest of the page already implies, and an unmeasured champion (below the backend's
sample floor, or `JUNGLE`) is silent for the same reason it must not read as either. Nothing changed behind it:
`/champions/{id}/roam` still computes and returns @5/@10/@15, the page still fetches all three, and reviving a
curve means writing a component, not a backend.

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

---

## Data & storage

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

**Champion aggregates are replace-by-scope on *live* patches only. Old patches are frozen and must never be wiped.**
Retention deletes `match_participants` past `RetainedPatchCount` (default 2). The cleanup set originally
spanned all patches, so every aggregation run deleted old-patch scopes and could only rebuild those still
inside the retention window — permanently destroying history, since no source rows remain. The aggregate *is*
the history for that patch; the accepted trade-off is that a frozen patch can never be recomputed — #466,
reaffirmed by #606 and #694.

**Aggregate retention is opt-in per environment: `AggregateRetainedPatchCount` defaults to 0 (frozen forever); preprod sets 2.**
The freeze is right for production history but preprod must stay tiny — #711.

**Timeline snapshots are pruned to the canonical marks {5, 10, 15, 20, 30} once a match is powerspike-aggregated.**
The dense per-minute grid grew to ~13 GB / 55.6M rows for exactly one consumer (power spikes); every other
reader uses only those five marks. Pre-aggregating power spikes first made the intermediate minutes
disposable — #772, #694.

**Aggregation is incremental per match, flagged on `matches`, never a full recompute.**
Full-recompute self-joins reached ~21.6 min per cycle on prod, making it 5.7× slower than preprod and
ingesting fewer games per day. The flag also dies with the match, so old-patch stats freeze naturally.
⚠️ A migration adding such a flag **must backfill existing rows to `true`** *when the aggregate it gates was
already populated by a full recompute* — otherwise the first incremental run double-counts — #811.
The mirror case: `matches.SynergyAggregated` (#922) ships `false` everywhere on purpose, because its tables are
created empty by the same migration and the whole retained history still has to be folded once. Read the flag's
question as "has this match already been counted *into this table*", not as a blanket rule.
Third case, `matches.BansAggregated` (#920): backfilled to `true` like #811 but for a different reason — the
source rows don't exist. Riot payloads aren't kept, so a match ingested before #920 has no `match_bans` rows at
all; folding it would add to the ban *denominator* while contributing no bans and deflate every rate.

**Ban rate is its own aggregate pair with a stored denominator, and `ALL` is a stored band — not a summed one.**
Bans come from every ingested match; `champion_aggregate_scopes` is per tracked account, so a ban count folded
in there would be a different population under the same roof. Hence `champion_ban_stats` (numerator) +
`ban_scope_totals` (denominator), folded together so a rate is always one cohort. The denominator is *stored*
because matches are retired after ~2 patches while aggregates are kept forever — once the matches are gone,
nothing else records how many there were. A match has no single elo band (`elo_bracket` is resolved per tracked
player), so it is counted once per band it touched **and** once in a stored `ALL` row: the bands overlap, so
summing them is not the match count. That is the one place `EloBracket.All` is persisted rather than being the
read-time union it is everywhere else — #920.

**No pick+ban "presence" figure, despite it being standard elsewhere.**
Pick rate's denominator is tracked mains' games at a lane; ban rate's is every observed match. The two are not
addable, and a presence number computed from them would be arithmetic without meaning. Offering a meta-wide
pick rate purely to make them addable was rejected: it would put two different pick rates on the same page —
#920.

**Dimension rows must be stored in a canonical order, not the order Riot reported them.**
`champion_dim_rune_pages` kept the two secondary perks in the player's selection order, so one page existed
as `(8451, 8444)` and `(8444, 8451)`. The 11-column unique index does not catch a permutation, so the page's
games and wins were split across both rows — roughly halving its displayed pick rate and distorting the
top-N. It reached 48% of the dimension (20 370 pairs) before anyone noticed, because the two rows render
pixel-identically. The primary tree was unaffected: there the selection index *is* the tree row. Ordering is
by perk id rather than tree row — the row is not stored backend-side and Riot assigns ids in row order
anyway. The backfill runs as a pipeline step, not a startup migration, because it rewrites hundreds of
thousands of pattern rows and prod migrates on startup. It also normalises pages that were never duplicated:
left in click order, the new canonical lookup would miss them and mint a second row, re-creating the bug —
#911.

**Lane win rate stores three counters and divides by the *decided* lanes, not by games played.**
A gold *threshold* at 15 min (`LaneOutcomeAggregation:GoldLeadThreshold`, 300) is what defines a won lane, and
a threshold necessarily creates a third outcome: lanes inside the band were neither won nor lost. Folding those
into losses would print "lane lost" where nothing was decided, so wins and losses are stored separately and the
rate divides by their sum. `LaneGames` is separate from the matchup's `Games` for a different reason: a match
with no ingested timeline, or one that ended before 15 minutes, is a game but not a judgeable lane — dividing
by `Games` would understate every lane win rate by the share of those. The threshold is configurable because it
is a product judgement, but changing it re-defines every stored counter and frozen patches can never be
recomputed (#466), so old rows keep the threshold in force when they were folded.
⚠️ #919's own premise was stale: it assumed the per-matchup 15-min leads were already aggregated from #606/#595,
but #889 dropped that aggregate with the "Lead vs role opponent" chart. What survives is the raw timeline
snapshot at the canonical marks, which is why the fold flag could ship `false` and pick up the whole retained
window instead of starting at deploy like #920's bans — #919.

**Matchups are a pre-aggregated read table — explicitly revising the earlier live-self-join design.**
#90 chose a self-join "for simplicity, not volume". Prod measurement showed the aggregate is bounded by
dimensions rather than games: ~22.2k rows, a few MB, versus a self-join over ~35 GB running single-threaded.
Reads became ~13-row indexed selects — #606.

**The disk figure sums Postgres and Mongo, because there is one disk** (#1023). The panel and the forecast
measured Postgres only, while Mongo holds the logs, crashes, audit events, Riot rollups, process runs and seed
requests — two of those (`audit_events`, `seed_requests`) with **no TTL at all**, i.e. the collections most
likely to surprise us were exactly the ones nothing watched. Verified on prod before building: every named
volume uses the default local driver and lands on a single `/dev/sda1` (193 GB), so the saturation date was
optimistic by construction. The daily rollup therefore takes one reading per engine and **sums** them; taking
the max — which is what grouping by day alone did — would have reported whichever engine is larger as the
disk. The snapshot grain gains an `engine` discriminator, and the upsert key becomes `(day, engine, name)`:
`process_runs` and `seed_requests` exist as both a (frozen) Postgres table and a Mongo collection, so without
it one engine's reading would overwrite the other's every day. Pre-#1023 documents are stamped `postgres`
on the next index pass — they are Postgres readings by construction, and leaving the field absent would make
the engine-filtered upsert insert a *second* document for the same day and table, silently doubling it.

**A step change in what is measured is not growth, so the forecast restarts at it** (#1023). The day Mongo
first gets measured adds its whole footprint at once. Fitted whole, the series reads that one-off jump as a
daily rate and predicts a saturation that is not coming. The forecast is therefore fitted only over the
trailing days that measure the *same set of engines* as the most recent one — so it goes absent for three
days after the change, which is a state the panel already explains, rather than confidently wrong for ninety.
Splicing the series or backfilling a Mongo number for days nobody measured were both rejected: the honest
answer to "we changed the instrument" is a gap, not a reconstruction.

**Ingestion throughput is measured from the run summaries, not from `matches.CreatedAtUtc`** (#1025). The
overview's existing chart buckets `GameStartTimeUtc` — when the games were *played* — which is a property of
the player population: it barely moves when ingestion stalls, and a backfill makes it grow in the past. The
portal therefore carried no signal for "did the pipeline keep up", which is exactly what the two retention
crash-loops (#982, #988) needed. `matches.CreatedAtUtc` exists and is the obvious source, but retention deletes
out-of-window and non-tracked-queue matches, so an old bucket shrinks over time and the curve rewrites its own
history. The `MatchIngestion` run summaries in Mongo are retention-proof by construction: deleting a match does
not rewrite the run that ingested it. The cost is a 180-day ceiling (the `process_runs` TTL), which the
response reports so the panel states the bound rather than drawing the tail beyond it as zero ingestion.
The two charts stay side by side and separately labelled — they answer different questions and merging them
would lose one.

**The counters are summed in memory because the summary is stored as opaque JSON text** (#1025).
`ProcessRunDocument.SummaryJson` is a string on purpose (#990: the admin receives byte-identical bytes to what
the recorder wrote), so Mongo cannot `$sum` a field inside it. The split is the one that plays to each side:
the server does the indexed `(processName, startedAtUtc)` range scan and projects two fields, the read parses
and does the arithmetic. Three consequences are deliberate. A run with **no** summary — failed, abandoned, or a
no-work pass whose shape differs — still counts as an attempt with absent counters, because dropping it would
make a crash-looping ingestor read as an idle one. Quiet periods **inside** the observed range are zero-filled,
since a stalled pipeline is the thing the chart exists to show. And nothing is filled **before** the oldest
surviving run: that period was not measured, and zeros there would assert a repose we have no record of.

**The candidate funnel measures Validated and Demoted, because Rejected is a status nothing assigns** (#1024).
The issue asked for a rejection counter, on the reading that rejections were the funnel's missing outcome. They
are not missing — they do not exist: `MainCandidateStatus.Rejected` is read in five places (the pruning
predicate, the harvest's refusal to resurrect, the manual seed's requeue list, the admin filter, the overview
breakdown) and **assigned in none**, so the `Rejected` bucket the portal has always shown is structurally zero.
Adding the requested counter would have shipped a permanently flat series dressed as a measurement. What the
funnel genuinely lacked was its *exit*: `AccountValidationService` promotes Processing → Validated without
feeding any summary counter, so nothing recorded how many accounts cleared ingestion. That counter
(`MatchIngestionSummary.AccountsValidated`) is what got added, alongside `MainAnalysisSummary.DemotedAccounts`,
which already existed and is the pipeline's only real negative outcome. Whether a rejection verdict should
exist at all — or whether the status should be removed, since several guards branch on a value that cannot
occur — is a product question, left to #1029 rather than smuggled into a chart.

**A forward-only counter renders as absent, not as zero, and key presence is what says which** (#1024).
`accountsValidated` did not exist before this deploy, so every `MatchIngestion` run already in the 180-day
window lacks the key. Summing absent-as-zero would have drawn months of "the pipeline validated nobody", which
is the exact failure #924 named. The series therefore reports the *first run whose summary carried the key* and
nulls every bucket before it. The boundary is global rather than per-bucket on purpose: past it, a period whose
only runs were no-work passes really did validate nothing, and a null there would hide a genuine stall behind
the same signal used for "not instrumented yet".

**`ValidatedAtUtc` had never been written in production, and the queue-latency snapshot is why that surfaced**
(#1024). The column, its migration and its API field had shipped long ago, but every Validated transition went
through `SetStatusForAccountAsync`, whose `ExecuteUpdateAsync` sets `Status` and nothing else — so the column
was null on every row, and the admin's candidate detail had been rendering an em dash for it since it existed.
The promotion now goes through a dedicated `MarkValidatedForAccountAsync` that stamps both in one statement,
rather than a flag on the generic setter: this is the only transition that owns that column. One behavioural
consequence is deliberate and was previously dead code — `PruneStaleNeverPromotedAsync` filters on
`ValidatedAtUtc == null`, so a candidate that was validated and later demoted is now genuinely exempt from the
stale prune, which is what "never promoted" always claimed to mean.

**Queue latency is a snapshot over retained rows and is labelled as one, rather than being faked into a series**
(#1024). `main_candidates` carries the three timestamps, so percentiles per past week look computable — but
retention prunes stale candidates, so any historical bucket would be computed over a survivor set that keeps
shrinking. The snapshot is the honest version of what those columns can answer, and it ships with its own bias
stated (pruning skews the survivors towards candidates that moved) and its sample count next to each percentile.
It takes no window parameter, which is the API making the same point structurally: there is no period to select.

**Daily storage snapshots go to Mongo and are keyed on the day, not the run.**
Storage history is append-only, time-ordered, ops-only telemetry with no relational joins — the exact
criteria that put logs and metrics in Mongo below — and a native TTL index prunes it for free instead of
needing its own retention arm in `MatchDataRetentionProcess`. Keying the document on `(day, table)` rather
than on the capture instant is what makes the writer safe to run every pipeline pass: prod runs the ingestor
`RunOnce` + `restart: unless-stopped`, so it re-runs back-to-back many times a day, and a day-keyed upsert
turns that into "refresh today's reading" instead of "append another point". No scheduler, no "have I run
today" guard, and a container restart can neither lose nor duplicate a day — #925.

**The disk forecast is absent rather than approximate when the data can't support it.**
It declines to project on fewer than 3 days of history, on flat or shrinking storage, on a crossing more than
a century out, and when no disk capacity is configured; the panel prints which of those applies. A forecast is
the one number on the admin that is invented rather than measured, and an operator who cannot tell a fitted
date from a placeholder will either ignore all of them or act on a fabricated one. Related: the projected
figure is measured `pg_database_size`, not the sum of `pg_total_relation_size` over public tables — the sum
excludes catalogs and would under-report what actually fills the volume that #680 filled — #925.

**Logs and metrics live in MongoDB, not Postgres, with two different guarantees.**
Mongo has native TTL retention and suits append-heavy time-ordered payloads. `logs` is lossy (bounded channel,
batched, drop-on-overflow — fine for diagnostics); `audit_events` is lossless and synchronous. Existing
Postgres log rows were deliberately not migrated — #416.

**Ops logs are signal-only: Polly retry noise is not persisted, domain events are.**
Every Riot 429 emitted `Execution attempt` + `OnRetry` warning pairs — dozens per minute while rate-limited —
burying everything useful — #444. This does not apply to `riot_api_call_rollups`: that collection exists
specifically to count every physical attempt including retried 429s (#93), a different data path from the
structured logs #444 is about.

**Riot API caller attribution uses an `AsyncLocal` ambient context, mirroring `IterationContext`.**
`riot_api_call_rollups` needed a "who spent this budget" dimension (#1035) but the three typed Riot clients
are shared `HttpClient`s across every `IIngestorProcess`, so there is no per-caller `HttpClient` to key on.
`Worker.RunModeAsync` opens a call scope (`ICallerContext.BeginCall(process.Name)`) around each process's
`RunCoreAsync`, the same shape `IterationContext` already uses for the pass-level iteration id; the metrics
handler reads it ambiently. The rollup's upsert key widened from `(bucket, endpoint, statusCode)` to include
`callerProcess`, so the unique index had to be dropped and recreated (`MongoLogContext.EnsureRiotApiCallIndexesAsync`)
rather than just adding a field — two different callers in the same minute/endpoint/status are two documents,
which the old 3-field unique index would have rejected as a duplicate key.

**The budget-headroom estimate (#1035) requires 24h of rollup history before it will extrapolate, and picks
the app rate-limit window with the smallest daily ceiling as "binding".**
Riot returns several simultaneous app-limit windows (e.g. `20:1,100:120`); the one that binds first under
sustained load is the smallest `limit * 86400 / windowSeconds`, not the one with the highest current-instant
usage ratio (that's what the existing app-rate-limit card already shows). Below 24h of observed history the
page renders an explicit "not enough data" state instead of extrapolating a daily cost from a thin window —
same reasoning as the disk forecast's absent state below. Implementation:
`RiotApiUsageQueryService.BuildHeadroom`/`ResolveBindingLimit` (`internal static`, unit-tested directly).

**Rank snapshots are capped at one row per account per UTC day (DB-level unique index).**
Intra-day LP granularity has no consumer. Accepted: rank history, match detail and the "nearest snapshot" elo
resolvers are day-precision — #907.

**`(GameName, TagLine, PlatformId)` on `riot_accounts` is a plain, NON-unique index. PUUID is the only real identity.**
A Riot ID is mutable and recyclable, so a stale row and a freshly renamed row legitimately collide. The unique
constraint made one collision roll back the whole `AccountRefresh` batch, which then reselected and failed
forever — the process was dead from 2026-07-26 — #901 / PR #902.

**PUUID indexing is intentional — do not propose dropping it or migrating to `RiotAccountId`-only.**
Standing instruction from the project owner. Related open chores (#123 LZ4 TOAST, #124 perk-selection PK)
are separate and still open.

**Pattern aggregates use a junction model (`champion_aggregate_patterns` + globally deduplicated `champion_dim_*`).**
This replaced both the original 23-column wide table (index maintenance on every column, a migration for every
new dimension) and the Sprint-5 per-scope dim tables, which had *lost cross-dimension correlation* — you could
not ask "when this player picks AD-crit Yasuo, what runes do they run?". Phase 6 restored it, exposed as
`GET /champions/{id}?buildId=` — `docs/phase-5-data-split-rfc.md`, `docs/phase-6-pattern-junction-rfc.md`.

**Postgres columns stay quoted PascalCase; only tables are snake_case.**
#227 proposed `UseSnakeCaseNamingConvention` for columns and was **closed as won't-do on 2026-07-28**: a
full column rename is a large, risky migration for a cosmetic gain, and raw SQL, prod psql habits and the
compiled model all rely on the current naming.

---

## Performance & incidents

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

**A matchup-scoped build page is folded live, not aggregated** (#923). `champion_matchup_stats` carries the
opponent dimension but only games/wins; the pattern aggregates carry the build data but are grained on
(account, champion, patch, platform, queue, position, elo) with no opponent — so a matchup build has no
aggregate to read, and adding an opponent dimension would multiply the scope rows by the roster. Measured on
production before deciding (patch 16.13, per champion x opponent x position, all elos): 49 411 pairs, median
**4 games**, p75 18, p90 79, p99 355, max 1 562, with only 24% of pairs reaching 20 games. Two consequences:
the live fold is cheap (a few hundred rows, capped at 2 000 for the tail), and **the binding constraint is the
sample, not the latency** — which is the opposite of what the issue assumed. Decided 2026-07-30: show the
matchup whatever its volume, with the game count on every variation, rather than hiding thin sections or
silently widening the window. The reader is told how thin the answer is instead of being given a fabricated one.

↳ **`/matchup` was the one place that didn't follow this, and now does** (#1075). The draft tool carried its own
`MATCHUP_SAMPLE_FLOOR = 8`: below it the matchup build was replaced by the champion's standard build behind a
warning banner, with the real build one "Show it anyway" click away. Against the measurement above — median
**4 games** per pair — that floor was withholding the matchup build for **more than half of every pair a reader
can select**, which is exactly the "hiding thin sections" this decision rejected. The build now always renders
and is qualified in place by `RecommendationPanel`'s existing low-sample icon, whose own floor (20 games) sits
well above anything the removed one caught. Only the genuinely empty case survives — no recorded game at all,
where there is no matchup build to show — and even that is now an icon-and-tooltip on the standard build's card
rather than a banner. **Caveats belong in a tooltip on the answer, not in a block above it**: a banner pushes
the thing the reader came for down the page and gets dismissed as chrome either way.

↳ **A figure about the matchup does not live inside the card about the sample** (#1098). #976 folded the lane
verdict into `RecommendationPanel`'s strip, on the reasoning that a win rate and a gold gap are one sentence
read at two points in the game. True of the reading, wrong about the lifetime: that card is a live query over
the retention window, and on the fallback path above it does not render at all — so a matchup with nothing
left in `match_participants` lost **every number on the page** at the exact moment the build under it was the
champion's global one rather than the matchup's. The matchup's record (games, win rate, matchup rate, lane
figures) now has its own strip on the page, mounted on champion / role / opponent alone; the card keeps only
what describes its own sample. `champion_matchup_stats` outlives the matches it was folded from, which is why
the strip still answers where the live query cannot. The general rule: **two populations, two strips** — a
figure belongs to the component whose mount condition matches the data's, not to the one it reads well beside.
Same PR, same reasoning applied to the fallback build: it renders the standard build's core and tree only, not
the champion page's variations and rune list, which are the champion's answer to a question this page isn't
asking and are read as the matchup's simply by sitting here.

**An expensive read path behind a TTL cache needs a single-flight, not a lock** (#870,
`Api/Services/RequestCoalescer.cs`). The cache protects steady state; the stampede happens on the first
request and at every TTL expiry, when concurrent callers all miss at once and each start the same scan — up to
50 000 scored accounts for one dedication ranking. Concurrent callers now share the in-flight `Task`. A per-key
lock was rejected: it serialises callers *and* still runs the work N times whenever the result is too large to
cache, turning a burst into a queue of full scans. The shared pass is detached from any one request's token —
a caller who walks away must not cancel the pass the others are waiting on — so only bounded work belongs in it.

**Postgres runs with `max_parallel_workers_per_gather=0` in every compose file — do not re-enable.**
2026-06-25 prod crash-loop: `53100: could not resize shared memory segment` — per-worker DSM segments
exhausting the container's 256 MB `/dev/shm` in bursts. EF read it as transient and retried, which under
`restart: unless-stopped` became an API + ingestor crash loop. Disabling beat raising `shm_size` because it is
deterministic (raising only moves the ceiling) and the API workload is index/PK reads — #589.

**Consequence: every heavy aggregate runs single-threaded, so batch work must be chunked.**
Three separate 300 s command-timeout incidents followed — pattern aggregation's `DISTINCT ChampionId` scan
(#603), timeline leads (#594), and harvest candidate aggregation (#632, dead since 2026-07-03). The last one
is instructive: *no code changed*, it was a slow threshold crossing.

**Aggregation is chunked per champion to bound memory.**
The ingestor was OOM-killed at ~6 GB managed heap because the source reader materialised every live-patch
participant *with* its item/skill event JSON and the builder held a second copy. An OOM bypasses the
per-process try/catch, and `restart: unless-stopped` replayed the same step — a crash loop that took the whole
VPS down — #600.

**A heavy `CREATE INDEX CONCURRENTLY` must never be a startup migration.**
The #595 covering index exceeded the migrator's `Command Timeout=300` (CONCURRENTLY also waits for the
ingestor's in-flight transactions to drain) → `TimeoutException` → API crash-loop retried on every restart →
deploy aborted on an unhealthy container, leaving an INVALID index that needed its own cleanup migration —
#597 / PR #598.
**Refinement, not a reversal:** once `match_participants` shrank to ~2M rows / ~11 GB after #680, the index was
rebuilt as a *plain transactional* migration — the timeout risk no longer held at that size, and
`CONCURRENTLY` plus a manual pre-create step meant prod's schema was not reproducible from migrations alone.
Accepted: a `SHARE` lock stalls the ingestor for the tens of seconds of the build — PR #750.
👉 The rule is about **heavy** index builds, not about partial single-column indexes on a table of this size.

**Npgsql pools are capped per service (api 50, ingestor 20) against Postgres `max_connections=100`.**
Two unbounded pools defaulting to 100 each could request 200 connections; once truemain.lol went live this
produced `53300: sorry, too many clients already` and a total API outage. PgBouncer was proposed as the proper
fix; the caps are what actually shipped (no `pgbouncer` service exists in the compose files) — #437, #461, #462.

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

**The EF compiled model must be regenerated on every schema change.**
`dotnet ef dbcontext optimize` → `Data/CompiledModels`. Originally for cold start; the operational reason is
that a stale model *silently drops columns*. Two concurrent schema PRs always conflict there — the second
merged must re-merge develop and regenerate — #242, `CLAUDE.md`.

---

## Infrastructure & deploy

**The admin portal is a standalone Nuxt app with its own deployment and domain — not a `/admin` route.**
Decided 2026-06-09. Auth is username/password + a signed httpOnly session; the app injects `X-Ops-Key`
server-side so the ops key never reaches the browser. Native composables, no Pinia — #96, #91, #376.

**Preprod auto-deploys from `compose.preprod.yaml` on every push to `develop`; prod auto-deploys from `compose.prod.yaml` only when a GitHub Release is published.**
Merging to develop or master alone never reaches prod. Both use the `hostinger/deploy-on-vps` action (a pure
API call, no SSH material in CI). Prod and preprod are on **separate Hostinger accounts**, so each needs its
own account-scoped token — `docs/prod.md`, `docs/preprod.md`, #717, #751.

**Images publish an immutable `:<sha>`/`:<version>` tag alongside the moving `:preprod`/`:latest`, and compose references the immutable one.**
The classic mutable-tag trap: the deploy job went green while the VPS kept running days-old containers,
because handing Docker Manager an unchanged compose spec never triggers a pull or recreate. Changing the
referenced image name every merge forces the pull. Doing so then surfaced a hidden GHCR `unauthorized`
failure the mutable tag had been masking — #738, #765, #767.

**Prod deploys from the version-controlled compose file — no hand-maintained host compose.**
A divergent host-only `docker-compose.yml` meant the pool-cap fix never reached prod, the uncapped pools kept
running and the `53300` outage returned — #462.

**Preprod and prod both apply migrations out-of-band, as a discrete CI step before the images roll — not at startup.**
`Database__ApplyMigrationsOnStartup` is `false` in both `compose.preprod.yaml` and `compose.prod.yaml`
(Microsoft advises against startup migration under concurrency: elevated app-account privileges, no review
or rollback). The `migrate-preprod`/`migrate-prod` jobs in `deploy-preprod.yml`/`deploy-prod.yml` generate an
idempotent SQL script from the deployed commit/tag and apply it over SSH by piping it into `psql` inside the
running Postgres container — neither VPS exposes a connection reachable from a GitHub-hosted runner. The
deploy job depends on the migrate job, so a failed migration blocks the image roll. Preprod runs this on
every merge to `develop`, so it catches a bad migration before it ever reaches prod. The credential is still
the app's own `POSTGRES_USER`, not a dedicated migration-only role — splitting one off is open follow-up
work, not this decision — #208, #246, #1058, `docs/production-migrations.md`.

**Preprod tracks `develop`, has its own Riot API key, and is deliberately tiny — a new key forces an empty database.**
PUUIDs are encrypted per API app, so key and database are an inseparable pair: old data is unusable with a new
key. Preprod runs every pipeline stage at reduced volume with 1-patch retention — `docs/preprod.md`, #705.

**OG image rendering is on, pinned to Satori + resvg, and deliberately reaches exactly two pages.**
It shipped disabled in #551 for one stated reason — "no dedicated share artwork yet, so the toolchain
would be build weight for no benefit". #926 supplies the artwork, which flips the benefit half but not
the cost half, so the setup stays narrow: the `.satori.vue` suffix pins the renderer (no `.browser.vue`
component exists, so playwright and a headless Chromium never enter the image), only `/champions/:id`
and `/truemains/:nameTag` call `defineOgImageComponent()`, and every render is cached for 1 h. That
matters because the renderer runs inside the web container on a VPS that has already been taken down by
one process's memory (#600) — the crawler-only traffic pattern is what keeps it cold. The **takumi**
renderer was rejected as still beta; a hand-rolled SVG→PNG route through the already-present `sharp` was
rejected because `node:*-alpine` ships no system fonts, so text would not render — Satori takes font
buffers directly, which is exactly the problem it solves — #926.

**OG image URLs are signed with a secret regenerated at every build, and that is left as the default.**
Without a secret, the encoded URL params are attacker-controllable, including the module's `html`
option — an arbitrary-HTML renderer on our own origin. Pinning a stable `NUXT_OG_IMAGE_SECRET` would
have to happen at *build* time (the module reads it in `setup()`), i.e. a Docker build arg plus a CI
secret. Accepted consequence instead: URLs minted by a previous build return 403 after a redeploy, which
only bites a re-unfurl of an old message — Discord and X have long since cached the bytes, and any fresh
unfurl re-reads the page and gets the current URL. Revisit if broken previews on old links are ever
reported — #926.

**Caddy terminates TLS and is the only public entry point in prod.**
Admin login was impossible over cleartext HTTP because the session cookie is sealed `Secure` in production
builds. Caddy also normalises `X-Forwarded-For` (dropping client-supplied values), which is what makes the
admin brute-force throttle non-spoofable. DNS must be **DNS-only, not Cloudflare-proxied**, or the ACME
challenge fails — #433, #430, #426.

**The admin `/analytics` iframe stays on Umami's public share view, not the authenticated app — kept as-is on purpose, 2026-08-04.**
The authenticated app *can* be framed: Caddy rewrites `analytics.truemain.lol`'s `frame-ancestors` to allow
`admin.truemain.lol` (`Caddyfile`), and the two subdomains share `truemain.lol` so the Umami session cookie
would reach the iframe. That rules out CSP as the reason to prefer the share view. The owner chose to keep it
anyway: the share view renders with no Umami login, while the authenticated app would show Umami's login
screen inside the iframe whenever no session is active. Session replays/heatmaps (#1013) — absent from the
share view's hardcoded nav because a share link is an unauthenticated public URL and a replay is a full DOM
recording — stay reachable only via the deep links added in #1014, opened in a new tab. Revisit if the
login-in-iframe friction becomes the bigger annoyance — #1013.

**Umami session replay/heatmap rows are purged after 7 days by a sidecar container, not left to grow.**
Self-hosted Umami has no built-in retention for `session_replay`/`heatmap_event` — the `retentionDays` label
in its client bundle only surfaces on Cloud-subscription screens. Both are the heaviest tables Umami writes
(a replay is a full stream of DOM mutations), and the sample rate was raised to 100% (owner's call — current
traffic is low enough to afford it). Left unbounded, this repeats the disk-fill shape of #680 (Postgres hit
68 GB, VPS ran out of disk). `umami-replay-cleanup` (all three compose files) is a `postgres:17.2-alpine`
container with its entrypoint overridden to just loop a `psql` purge once a day — no server started, matching
`umami-db`'s Postgres major version. Retention is `UMAMI_REPLAY_RETENTION_DAYS`, default 7 — #1018.

**`/ops/*` is the only authenticated API surface** (`X-Ops-Key`, min 32 chars, rotated independently of the
Riot key). Everything else is public and rate-limited to 100 req/min per IP — `docs/api.md`.

**The Riot API key is a permanent *personal* key — not a 24 h dev key, and not production-approved.**
(Owner-confirmed terminology, 2026-07-28: do not call it a dev key.) The production application is submitted
and pending review. It declares ACCOUNT-V1, SUMMONER-V4, MATCH-V5, LEAGUE-V4 and CHAMPION-MASTERY-V4 —
**not SPECTATOR-V5**. Consequences: no live-game features (#532, parked P3), no RSO and therefore no user
accounts (#780), ingestion is rate-limited, and data changes are forward-only because backfill is not
possible. Approval is the single external unlock for all of it — #780.

---

## Conventions

- **Language split**: talk to the user in French; everything committed or published (code, comments, commits,
  issues, PR titles and bodies) is in English. (`docs/api.md` is in French, predating the rule.)
- **`develop` is the default branch.** All PRs target it; the only PR allowed to target `master` is the release
  PR. Feature PRs are **squash**-merged; release PRs use a **merge commit**, because squashing there creates
  false conflicts on the next release. `develop` survives release merges and is never deleted.
- **Branch names** `<type>/<issue>-<short-kebab>`, conventional commits, no `Co-Authored-By: Claude` trailers,
  no "Generated with Claude Code" footers, `Closes #N` in the PR body.
- **A PR is done** when CI is green, the Claude review verdict is clean *on the current head SHA* (verdicts
  trail ~1 commit behind pushes), and every real finding is fixed or rebutted — then merge without asking.
  Stop after ~3 non-converging iterations and report blockers instead of looping.
- **CI traps**: backend CI builds **Release** with analyzers as errors (Debug is not enough); `nuxt typecheck`
  can pass on stale `.nuxt` types while CI's `nuxt build` fails; `web/package-lock.json` must be regenerated
  with `npx npm@11.13.0` (older npm omits sharp optional deps).
- **API wire conventions**: camelCase JSON, RFC 7807 problem details on all 4xx/5xx, no global `/api` prefix,
  `patch` normalised to `major.minor` (invalid values treated as unfiltered), canonical Riot position values,
  `pageSize`/`limit` ≤ 0 means "default" — `docs/api.md`.
- **Every issue goes on GitHub Project #2.** Scheduling and urgency are two separate fields: **Sprint** (the
  14-day iteration field) says *when* the work is planned, **Priority** (P0–P3) says how urgent it is and
  orders work inside a sprint. Priority used to double as the sprint bucket ("P0 = current sprint"); that
  overloading was dropped because it silently competed with the real iteration field the board was already
  using. No milestones.
- **A health panel may not pass what it did not measure** (#924). Detector verdicts are green / amber / red /
  **unknown**, and `unknown` never means "fine": an unmeasurable row outranks green in the roll-up (one
  unchecked platform must not let a card claim to be clean) but stays below red (it must not hide a real
  failure either). Headlines are worded from the verdict, not from the count, so a card can never read
  "everything completed" while its colour says "not measured". The mirror-image rule matters just as much: a
  signal that is *deliberately* unavailable — a starter basket's canonical order (patch-dependent prices), a
  trend with no previous window, a patch too new or too old to compare — is shown as a row but **does not
  vote**, because a card pinned to unknown for ever teaches the operator to ignore its colour.
- **A detector shares the repair's definition of the bug it audits** (#924). The canonical-key SQL lives once,
  in `Data/DataQuality/ChampionDimensionCanonicalKeys.cs`, and is read by both the ingestor's
  `RunePageDeduplicationProcess` and the admin duplicate detector. Two copies would eventually disagree, and a
  detector that groups differently from the repair reports a clean bill of health for a live bug (#911).
- **A row rendered on more than one surface sizes off its own width, not the viewport** (#967).
  `MatchRow` and `LeaderboardRow` are `@container`s. The same row sits full-width on a page, in a ~33rem
  drawer and in a sidebar, so a viewport `xl:` breakpoint told the narrow copy it owned the page and its
  fixed columns spilled into its own `overflow-hidden` clip — invisible on the surface it was tuned for,
  broken everywhere else. Content degrades by tier as the row narrows (compositions, then secondary stats,
  then the loadout wrapping onto a second line) rather than being cut off. `pages/dev/match-row.vue` renders
  the row at each tier width so the compact layouts are reviewable without reproducing the host surface.
- **Detector thresholds are configuration, not constants** (`DataQualityDetectors:*`). The honest line differs
  between preprod and production, and an operator silencing a crying-wolf card must not need a redeploy. A
  level of `0` disables it, which is how a warning-only signal is expressed. They are **stated in words with
  their direction** ("warning above 8 h", "warning below 40% of the median") rather than printed as a pair of
  numbers: the payload carries a `direction`, because a floor rendered like a ceiling inverts its meaning and
  nothing in the number itself says which it is.
- **A health panel answers before it reports** (#992). `/data-quality` opens on one verdict line, then the
  detectors as a severity-ordered list — one line each, passing checks collapsed behind a single disclosure —
  with rows, thresholds and source notes behind a per-detector expand. The first cut printed everything every
  detector knows on every detector at once, so five passing checks were as loud as five failing ones and the
  page had no shape; the operator could not tell, on arrival, whether anything was wrong. **Colour states the
  current reading, never a configured constant**: amber and red on threshold values put warning colours on a
  healthy panel, which is how a page teaches its reader that its colours mean nothing. Everything the cards
  carried is still one click away — legibility here is ordering and defaults, not removal.

- **The health cockpit (`/health`, #1031) holds no depth of its own — every tile is a link, and the verdict
  is judged server-side.** Answering "is the pipeline healthy right now?" used to mean opening four pages
  (`/`, `/processes`, `/data-quality`, `/database`). `PipelineHealthEvaluator` is pure (no DB, no clock) and
  reuses `DataQualityDetectorEvaluator`'s `DetectorStatus`/`Worst` precedence on purpose, so the cockpit's dots
  and the pages it links to cannot disagree about what "amber" means — a tile that judged a signal differently
  from the panel it points at would be lying. Two of the four signals are lifted verbatim from the existing
  `/data-quality` detector payload (data quality rolled up, ingestion lag copied through as its own top-level
  tile) rather than re-measured; only the disk forecast gets its own knobs (`PipelineHealth:DiskForecastAmberDays`/
  `RedDays`, validated amber ≥ red on boot) because "how many days out is close enough to act on" is a call the
  storage panel's own `StorageHistory:ThresholdPercents` doesn't make. The green/amber/red/unknown vocabulary was
  extracted from `DataQualityDetectorItem.vue` into `admin/shared/utils/detector-status.ts` at the same time, for
  the same reason: two panels painting the same four statuses from private tables is how they drift.

- **A process that has never recorded a run is `unknown`, not amber; an abandoned run is a warning, not an
  error** (#1031). The cockpit's process signal deliberately does not judge *how long ago* a process last
  succeeded — the ten pipeline processes run on wildly different cadences, and inventing a per-process
  expectation here would be a second, competing source of truth for "the pipeline has stopped", which is what
  the ingestion-lag detector and raw-data freshness already answer. `Missing` (no run ever recorded) rolls up
  as unknown rather than green precisely so a fresh environment doesn't report ten processes as healthy;
  `Abandoned` (the run's host died mid-flight, outcome unknown) is a different claim from "it ran and failed"
  and is coloured accordingly.

---

## Where API reads live

**Decided 2026-07-30 (#865): the documentation was stale, and now matches the code.** API reads are
purpose-built **query services** returning read-models, living in `Api/Services/<area>` beside the endpoints
they serve, injecting `TrueMainDbContext` and projecting with `AsNoTracking`.

The rule had said reads live in `Data` with `Api` staying persistence-ignorant. Measured on 2026-07-30, 83
`*QueryService*.cs` files sat under `backend/Api/Services`, 36 of them injecting the context, while
`backend/Data` held only Mongo-side query objects (`MongoLogQuery`, `CrashQuery`, `RiotApiUsageQuery`). The
contradiction was re-litigated on six separate PRs, each time resolved by following the code and leaving the
doc alone — which is the right short-term reflex and the wrong durable answer.

What was kept, because it held everywhere and is the half that matters: **no generic `IRepository<T>` for
reads.** Every read path is shaped by the question it answers, returning a read-model rather than entities.
The rejected alternative was migrating 36 services (plus tests and DI) into `Data` for an architectural
benefit no user would ever see.

`Data` still owns: the schema (entities, configurations, migrations, the compiled model), the Mongo-side query
objects, and **SQL that must not diverge between two consumers** — e.g.
`Data/DataQuality/ChampionDimensionCanonicalKeys.cs`, read by both the ingestor's rune-page repair and the
admin duplicate detector (#924). That last one is the durable reason `Data` holds any query text at all: a
detector that groups differently from the repair it audits reports a clean bill of health for a live bug.

Counts drift with every PR — re-measure rather than trusting the numbers above:

```bash
find backend/Api/Services -name "*QueryService*.cs" | wc -l
grep -l "TrueMainDbContext" $(find backend/Api/Services -name "*QueryService*.cs") | wc -l
```

---

## Admin-portal data lives in Mongo, not Postgres (2026-08-01)

**Decision:** everything stored *for the admin portal* belongs in the Mongo observability database
(`truemain_logs`), not in the SQL schema. Postgres keeps the TrueMain product data — matches, accounts,
candidates, aggregates — i.e. what ingestion produces and the public site reads. This finished what #416
(logs), #93 (Riot usage), crashes and #925 (storage snapshots) started: the last two admin-only tables,
`process_runs` and `seed_requests`, moved to Mongo collections of the same names.

Consequences that matter later:

- `process_runs` finally has retention (180 d TTL index on `startedAtUtc`) — the SQL table had none and grew
  forever. `seed_requests` has **no** TTL: it is a functional operator queue, not telemetry, and its store
  **throws** when Mongo is unconfigured instead of silently dropping an operator's request (the
  observability stores no-op by design).
- The ManualSeed claim (`Pending→Resolving`) is a single guarded Mongo update — same no-TOCTOU semantics the
  SQL `ExecuteUpdate` gave. The Riot-ID idempotency check uses a strength-2 collation (case-insensitive,
  accent-sensitive) instead of escaped ILIKE.
- Run summaries are stored as **raw JSON text** (`summaryJson`), not nested BSON, so what the admin renders
  is byte-identical to what the recorder serialized — no BSON→JSON number reshaping.
- Documents store Guids and enum names as plain strings, deliberately: shell-readable, and the one-shot
  SQL→Mongo backfill stays a dumb `row_to_json` transform.
- The SQL tables were **not dropped in the same PR**: the code switch shipped first (no schema change, no
  compiled-model churn), the historical rows were copied preprod/prod once the frozen tables had no writers,
  and a follow-up PR drops the tables + regenerates the compiled model.

**Champion-page icons are slow because of browser queue depth, not the image proxy — measure the split before
"optimising" it.** The obvious reading of a slow champion page (~118 `/_ipx/**` requests, ~600 KB) is that the
proxy or Riot's CDN is slow. Splitting per-request timing on preprod says otherwise: **queue 2459 ms, server
65 ms, download 1 ms**, and the proxy answers 40 concurrent requests in 0.65 s. The cost is the browser holding
a burst of ~106 distinct, equal-priority image requests issued in one tick when the API data lands. So a
persistent/disk cache and a boot-time pre-warm were both **rejected**: they buy back tens of milliseconds of an
850 ms budget, while a disk cache on a public, unauthenticated route that accepts arbitrary modifiers is the
same disk-exhaustion class that already crash-looped this box (#680). Pre-warming was rejected additionally
because it fires ~500 requests at Riot and the volunteer-run CommunityDragon mirror on **every** boot, and this
stack has had restart loops. What is left is payload size and queue depth — #997.

**`SkeletonImage` serves WebP; `RankIcon` deliberately does not.** At the canonical 64×64 fetch size the live
assets go champion 10194 B → 1100 B, perk 8933 B → 3396 B, item 6096 B → 2130 B with no visible difference —
the perk icons (thin bright line art over transparency) are the demanding case and survive it. It is **not**
applied globally: `RankIcon`'s sources are `.svg` and IPX passes them through as `image/svg+xml` today, so
forcing a raster format would trade a vector that stays crisp at any DPR for a 20 px bitmap. This is a
format decision inside the existing `<img>` + `useImage()` split, not a change to it — the `@nuxt/image`
policy (fixed-size icons use `<img>` + `useImage()`, real responsive images use `<NuxtImg>`) still stands.

**Every icon URL is built by one helper, so one asset is one cache entry.** `useCanonicalIcon()` is the
only place that decides fetch size and format; `SkeletonImage` calls it, and so does each component that
deliberately renders a plain `<img>` instead (lane glyphs in leaderboard/profile rows and match rows, the
search palette's trailing icons — fixed-size glyphs appearing dozens of times per page, where one
component instance per icon costs more than it gives). Hand-writing `ipx(...)` per call site is what this
replaces, and the drift was real: the same position glyph was being fetched at 12, 20, 22 *and* 64 px —
four downloads and four cache entries for one image — while the search palette bound the **raw Data
Dragon URL**, shipping a 120×120 PNG (30 267 B, straight from Riot's CDN, uncached by us) into a 20 px
box. Measured on preprod after the change, that icon arrives in **1 446 B**.

Note the number the canonical size deliberately gives up: fetched at the palette's own 20 px it would be
306 B, roughly five times smaller again. It is fetched at 64 px anyway, because a *second* size is a
second cache entry — the same champion portrait already exists at 64 px from every other page, so the
canonical URL is usually a cache hit costing nothing, while a bespoke 20 px variant would always be a
fresh download. Sizing per call site is the local optimum and the global mistake; that is the whole point
of the helper. `RankIcon` remains the one deliberate exception, for the SVG reason above — #1000.

**The `/_ipx/**` cache evicts by patch, keeping the current patch and the two before it.** Every source URL is
patch-pinned, so a release turns the whole catalogue over at once and strands the outgoing patch's bytes in the
64 MB budget precisely when the cache is cold. The sweep runs **only** when a newer patch is first observed, not
as a check on every write: the champion page has a patch filter, so old-patch URLs are legitimate traffic, and
evaluating expiry per write would store and immediately drop each of their icons, leaving old-patch browsing
permanently uncached. The window is the three newest patches *observed* rather than `newest - 2` arithmetic, so
a season rollover (16.1 after 15.24) keeps the right three — `server/utils/ipx-patch-retention.ts`, #997.

## The configuration viewer is an allow-list, and each host reports itself (2026-08-08)

**Decision:** `/ops/configuration` (#1034) exposes sections through a hand-declared allow-list
(`EffectiveConfigurationCatalog`) that names an options type and, when needed, an explicit
`IncludeProperties` subset — never a full options dump filtered for secrets afterward. A deny-list is one
forgotten property away from returning the Riot key, connection string or `X-Ops-Key`, and the property
that leaks is always the one added after the filter was written. A name-shaped backstop
(`EffectiveConfigurationRedaction.IsSecretName`) drops anything credential-shaped that slips into a
catalog entry anyway, and a unit test walks both production catalogs asserting that backstop never has to
fire — so a new section with a secret-shaped property fails a test, not a review.

**The Api and the Ingestor report themselves through two different mechanisms, on purpose.** The Api reads
its own `IOptions<T>` live, on every request — it can introspect its own container, so there is nothing to
cache. The Ingestor cannot be introspected by the Api (different container, and its options classes live
in an assembly the Api does not reference), so it publishes a snapshot of its bound options to Mongo
(`effective_configuration`, one document per process, **overwritten** at every boot — it runs `RunOnce` +
`restart: unless-stopped`, many boots a day, and the page wants "what is it running now", not a log of
every past boot) via a boot-time `IHostedService`, registered ahead of the pipeline's own worker since a
fast `RunOnce` pass can end the process before a later-registered service would run. Consequence: the
Ingestor's "as of" timestamp can be older than the page load without meaning anything is stale, while the
Api's is always "now" — the page states this per process rather than presenting both timestamps the same
way.

**"Servable" is a share of the settled patches' median, never an absolute line count.** The patch-coverage
verdict (#1033) asks whether enough `(champion, lane)` lines clear `ChampionsList:MinSampleGames` for the
directory and tier list to mean anything — and the honest bar for that moves with the corpus. The number of
lines clearing ten games grows every time tracked accounts are added, so a hard-coded "300 lines" would read
permanently green on production and permanently red on preprod, which is the same as having no check. The bar
is therefore `PatchCoverage:ServableLinesRatio` (0.6) of the **median of the patches strictly older than the
one being served** — the settled ones. The served patch and anything newer are excluded from their own
reference on purpose: a still-filling patch dragged into the median pulls the bar down to whatever it
currently is, and the check goes green on an empty patch. That is the same "the edge patch is not comparable"
rule the patch-volume detector already applies (#924). `PatchCoverage:ServableLinesMinimum` is the fallback
when a database holds a single patch (preprod's normal state) — crude, and still an answer rather than a
shrug.

**A fold that shipped mid-corpus reports `null`, never `0`, on the patches it predates.** Raw match payloads
are not kept, so #920's bans and #957's per-opponent spikes can never be backfilled: their absence on an older
patch is a property of when the fold shipped, not a failure. Printing `0 rows` there sends an operator hunting
a bug that does not exist, so the read model carries `measured: false` with the first patch the fold ever
wrote a row on, and the page prints "not measured before *patch*" **in place of** the counts rather than
beside them. The first-measured patch falls out of the same grouped scan that produces the per-patch numbers,
so distinguishing the two costs nothing — `PatchCoverageQueryService`, #1033.

## The rose-gold-only surface rule is reversed: neutral surfaces, a scarce accent, and a data axis of its own (2026-08-10)

**Decided in #1060 (part of the #1059 redesign), reversing the "surfaces are rose-gold-only" rule that
`main.css` had carried as the successor to an earlier emerald-only one.** The old rule paired a warm accent
(`rosegold-400 #e58f83`) with a warm neutral (`mauve-900 #211d1e`) and forbade any second hue on a surface.
The accent therefore sat on a background already halfway to its own colour, so nothing separated: the site
read as one flat warm mass. What replaced it:

- **`ink` replaces `mauve`** — a near-neutral, faintly cool charcoal. The `rosegold` ramp is **unchanged**;
  it simply reads far more saturated once nothing around it is warm. The fix was never the hue.
- **Rose gold is scarce on purpose**: brand and interaction only (logo, active nav, focus rings, primary
  buttons, links, selected states, the hero accent word). It never colours a data value and is never a
  generic surface tint. Scarcity is the whole mechanism — an accent applied to everything is not an accent.
- **Measurements get their own cold→warm axis** (`--color-data-*`: teal good → neutral → amber bad), and
  unlike the `--color-stat-*` tooltip vocabulary it **is** allowed as `bg-*` / `ring-*` / `border-*`. The
  former rule banned semantic colour on data outright, which on a stats site gave away half the legibility:
  a 52% and a 9% rendered identically. Green/red was rejected rather than overlooked — `rosegold-500
  #d9736c` is itself a desaturated red, and a red "loss" beside the accent in a dense table is a coin flip
  to read. Teal and amber share no hue with the brand, so a coloured number can never be mistaken for an
  interactive one. The activity heatmap moved onto this axis at the same time, retiring the #927 reasoning
  that a second hue on the grid would be an intrusion.
- **The tier ladder rides the same axis.** Its medal metaphor (rose-gold → gold → silver → bronze → iron)
  broke twice over once amber meant "bad": A and C read as warnings, and `tier-s` was *literally*
  `rosegold-400`, giving the best tier the brand colour and no comparative meaning at all.

> ⚠️ **The two bullets above were reversed on 2026-08-11 — see the entry below.** The cold→warm data axis and
> the teal tier ladder are gone; measurements are rose gold again and the medal ladder is back. Everything
> else in this entry (ink surfaces, the four-step opaque elevation, `surface` replacing `glass`, dark-only)
> still stands.
- **`surface` replaces `glass`** at all 55 call sites plus the global `UCard` / `UBadge` themes. Translucency
  everywhere meant nothing was ever *on top of* anything. Paired with it, the elevation ladder was
  un-flattened: `--ui-bg-muted` and `--ui-bg-elevated` had both pointed at `neutral-800`, so the whole app
  had two levels — page and not-page — and depth had to be carried by borders that were themselves
  translucent. There are now four distinct opaque steps. `glass` is removed outright: it was kept at first for
  the home hero, but the hero's own search field reads better solid against the eclipse, which left the
  utility with no call site — and a material the docs call load-bearing with nothing using it is how a design
  system starts lying about itself.
- **A second family carries the numbers.** `--font-mono` had been deliberately aliased to Inter; it now
  points at Geist Mono, used by the `stat-value` / `stat-label` utilities. The old scale put a value and its
  label one step apart (`text-sm` over `text-xs`, same family, same weight), so a dense row read as noise.

## Measurements are rose gold again: the cold→warm data axis is withdrawn (2026-08-11)

**Decided by the product owner in #1096, reversing two bullets of the #1060 entry above** — the `--color-data-*`
cold→warm axis and the teal tier ladder. Everything else #1060 shipped (ink surfaces, four opaque elevation
steps, `surface` over `glass`, dark-only, the scoped eclipse) is untouched and stays.

The teal was doing what a two-hue scale is meant to do. The call was not that it failed at its job, but that
the site should read as rose gold and should not carry a cyan it never wanted. Recorded plainly because the
#1060 reasoning is still on this page and will read as current otherwise.

- **The axis is one-sided now.** `--color-data-good` is `rosegold-400`; below average simply steps down the
  neutral ramp (`--color-data-bad` is `ink-500`). A losing win rate is *not* flagged in a warning colour, it
  is merely not highlighted. Consumers did not change — `rate-tone.ts`, `StatBlock`, `MetricBar` and
  `TierBadge` all read the same tokens, so this was a token edit, not a component sweep.
- **The medal ladder is back** (rose gold → gold → silver → bronze → iron). #1060 retired it on the grounds
  that gold and bronze are amber and amber meant "bad"; with the warm end of the axis gone, the collision it
  was avoiding no longer exists. `dedication.ts` and `PlayerPerformance.vue` read `--color-tier-*` directly,
  so the dedication ranks and performance verdicts followed for free.
- **The activity heatmap returns to rose gold / neutral**, which is where #927 had it. The sign of a period is
  now carried by *accent vs grey* rather than by two opposed hues, which puts more weight on intensity: a
  one-game losing period is a faint grey cell. That is the intended read — it is barely a signal.

**The cost, stated so nobody re-derives it in surprise: the accent is no longer exclusive to interaction.**
#1060's central mechanism was that rose gold meant "you can touch this" and nothing else, which is what let it
stay legible while scarce. A rose-gold number is now a *good* number, not a clickable one. Affordance has to
come from shape and position — a border, a cursor, a control's own chrome — and never from hue alone. Any
future "make it obvious this is clickable" that reaches for the accent alone will not work any more.

**The eclipse is scoped to the home hero** (`AppBackdrop.vue` moved out of `app.vue`). As a viewport-fixed
layer behind every route its corona passed *through* the champion and leaderboard tables: rows near the glow
rendered a visibly different luminance from those outside it, and a table that changes brightness down its own
length cannot be scanned. The signature is kept where the page's job is atmosphere, and dropped where the
page's job is numbers. Share cards keep the eclipse regardless of the page they were shared from — a share
card advertises the site.

**Light mode is gone** — the toggle, the five `dark:` variants, and the `.dark` scoping of the surface
tokens. It was never designed or tested (five variants in 119 files), and keeping it half-defined meant
every future token decision had to be made twice. The module has no "forced" switch — `preference` is only a
default — so the colour-mode `storageKey` was moved at the same time, retiring the `light` value a returning
visitor might still carry from before the toggle was removed.

Reviewable at `pages/dev/design-system.vue`, which is the compensating control for having no Storybook and no
SFC-mounting test setup: every token, elevation step and material on one screen, stripped from production
builds like the other `dev/*` playgrounds.

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
- **The homepage volume chips span two patches** (`ChampionsList:HomepagePatchWindow`), the served one and the
  one before it, so the headline figure doesn't fall by an order of magnitude every two weeks — which reads as
  data loss, not as a patch boundary. The chips **name the range** they cover. The tier-list teaser beside them
  does *not* merge patches: a tier is a percentile within one patch's field, and a blended ranking would
  describe a meta that never existed. That asymmetry is deliberate and is why the two read from different calls.
- **The window never reaches forward.** It starts at the served patch, so games on a patch every other surface
  is refusing to show are not advertised on the homepage either.

Player- and champion-scoped views were never affected: `ChampionScopeLoader` has had
`ResolveLatestPatchAboveFloor` since long before this, for the same reason. #1109 is that idea finally applied
to the global reads.

Shares a trigger with #1107 (CommunityDragon's unpublished patch branch aborting the folds) but nothing else —
and fixing #1107 makes this one *more* urgent, since the folds now succeed on patch day and the flip onto a
thin patch would happen sooner.

## Keeping these files current

A PR that ships a user-facing feature, removes one, or reverses a decision here **must update
`features.md` / `decisions.md` in the same PR**. These files are the context a fresh session loads instead of
re-reading the codebase; stale entries are worse than missing ones.

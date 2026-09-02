# TrueMain — decision log

Settled product and architecture decisions **with their rationale**, so a session doesn't re-litigate them or
propose something a past incident already ruled out. Companion file: [features.md](features.md) (the *what*).

Format: **Decision** — why — `source`.

Last verified against `develop` on 2026-07-28.

---

## Product

**Stats are computed from *true mains* by default, and that default is now a filter rather than the
pipeline's only setting.** Averaging all games drowns the specialist signal. A player is promoted to
"true main" through a games-vs-mastery investment signal. This is the thesis of the site — `README.md`.
Until #1346 it was also a hard filter at ingest (`where stat.IsMain` in the source-row reader), so the
aggregate physically could not answer "what does everyone build". It now carries both populations and the
reads choose; the thesis is unchanged, it is just no longer enforced by absence of data.

**The champion pages open on Master+, not on every rank** (#1346). "All ranks" was never a skew in
practice — production is already 82% Diamond-and-above, and Master+ alone is ~72% of the games on the live
patch — but a blended average across every tier is the one thing the site exists not to serve, and opening
on it made the promise depend on a filter the reader had to find. Measured before switching: every one of
the 173 champions clears the build sample floor on its dominant position but two, and those two fall back
to the existing thin-sample warning rather than an empty page. The default is **per page**, not global: the
player-scoped champion page passes `ELO_BRACKET_ALL`, because its scope is already one account and
re-slicing that by rank empties the build of any truemain below Master.

**The truemains population is one boolean on the scope row, not a duplicated aggregate** (#1346).
A scope is keyed on (account, champion, platform) — exactly `main_champion_stats`' own key — so main-ness
cannot vary inside one row. That is what lets the filter be `WHERE "IsMain"` over a single population where
the unfiltered read is the superset, instead of storing the mains twice. The flag is frozen with the rest of
the slice: it records what the account was when the aggregate was built, like every other number on the row.

**The patch the site serves is resolved over mains only, never over the selected population** (#1349).
`ResolveActivePatchAsync` and the two reads behind it (`LoadPatchesNewestFirstAsync`,
`LoadLinesPastFloorAsync`) were missed by #1346's audit. They are pinned to mains for the same reason they
carry no elo clause — changing a filter must not move the patch the whole site serves — and for a second one:
`MinServablePatchLines` is #1109's anti-thin-patch floor, and a patch clearing it on non-main volume would be
served to the default, mains-only directory while its truemain sample is still too thin to show.

**Widening the aggregate meant auditing every existing read, because the dangerous direction is silent.**
Adding rows to a table that ~16 call sites already read means each of them quietly changes meaning the day
re-aggregation runs — the truemains leaderboard would count a player's off-main games, dedication would
measure a career the player never had, the homepage's "games analysed" chip would quadruple overnight. So
every pre-existing read states its population explicitly and keeps mains-only; only the three surfaces the
toggle drives (champion builds, the directory, the tier list) take the parameter. The exceptions are the
table-health reads in `Ops` (row counts, impossible-total detectors), which describe the table itself and
must see all of it. A default of `truemainsOnly: true` on the shared `WhereChampionScope` makes the safe
choice the one you get by saying nothing.

**The widened population is gated on a flag, because "a separate ops step" was not true otherwise**
(#1346, #1349). `ChampionPatternAggregationProcess` sits in `JobModeSequence.FullPipeline`, which the worker
runs continuously — so dropping the `IsMain` filter at the source did not create an ops decision, it created
a change that lands on the next cycle after a deploy. On production that is ~4.3x the source rows (438k →
1.87M) on the one process that once reached ~6 GB of managed heap and got OOM-killed with the VPS attached
(#601), whose per-champion chunking has never been measured at that volume. `MainAnalysis:AggregateNonMainPopulation`
(default **false**) is that gate: while it is off the pipeline folds mains only, exactly as before, and the
truemains toggle's "everyone" state returns the same rows as "truemains". Reads never branch on it — they
filter on the persisted flag, which is simply always `true` until someone turns the gate on.

**A demoted account's scopes are demoted, not deleted — once the widening is on** (#1346). The pipeline used to filter on `IsMain` at
the source, so an account that stopped being a main of a champion simply stopped producing rows and its
aggregates were purged on the next run. They now survive with `IsMain = false`. The guarantee that purge was
protecting — a demoted account's games stop counting towards the champion's truemain build — is unchanged; it
is the read's job now rather than the writer's.

**Matchups reject the widened population rather than ignoring it** (#1346). The matchup slice is folded from
an aggregate whose champion side is mains-only (#1087), so "everyone" is not an answer it can give.
`?truemainsOnly=false` combined with `?opponentChampionId=` is a 400, for the same reason a matchup without a
position is: quietly returning mains-only rows under an "everyone" label is a fabricated answer, not a
lenient one. The UI never reaches it — the toggle locks on while an opponent is pinned, and the filter
composable resolves the invalid pair away so a hand-edited URL renders instead of erroring.

**The thin-sample caveat is a header tooltip, and it counts games rather than coverage** (#1346). It used to
be a full-width `UAlert` above the build grid, and it also fired on `eloCoverage < 0.1` — which, once Master+
became the default, put an incident-sized banner at the top of the page for a slice that was perfectly well
sampled. It now rides the header's warning-triangle idiom (the one the retired-sample card and the builder
panels already use) and says only what a reader deciding whether to trust the build needs: how many games it
rests on. What share of the all-rank population the bracket covers is not that.

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

**Champion URLs are bare slugs, and the slug map is app state rather than per-page async data.**
Two calls, one visible and one not. The visible one: `/champions/ahri`, not `/champions/103-ahri`. The
id-prefixed form was genuinely tempting — the champion id parses straight out of the segment, so the page
needs no map at all and the whole resolution problem disappears. It was rejected on the one ground that
outlives the implementation cost: a URL is permanent once indexed and linked, and the prefixed form is
uglier forever to save work once.
The invisible one is what that choice then forces. A bare slug has to be resolved to an id *before* the
page can fetch anything, and every link on every page has to turn an id back into a slug. Doing either
asynchronously would mean the server renders `/champions/103` links and the client rewrites them to
`/champions/ahri` — a hydration mismatch on almost every page of the site, and a champion page whose
fetches all wait on a lookup. So the `championId → slug` map is loaded into `useState` by an **awaited
universal plugin**, before the first render, and every read of it is synchronous. The cost is one
Nitro-cached call per SSR render plus ~2.5 kB of payload on every page — which is why the endpoint carries
ids and slugs only and is not the existing champion list (~20× larger, and patch-keyed for no reason here).
The slug is DDragon's champion **key** lower-cased, never the display name: the key is stable across Riot's
renames, and deriving from the name would need punctuation rules for `Nunu & Willump` and `Bel'Veth` that
the sitemap, the links and the router would each have to implement identically.
The guard is a **route middleware**, not a check in `setup`: setup does not re-run when only a route param
changes, so a guard written there fires on a full page load and then silently stops firing on every
client-side navigation after it. It 404s an unknown segment rather than rendering the empty-build state —
that state tells a crawler the URL is real and merely thin — and 301s the legacy numeric and mis-cased
forms, which keeps every pre-#1124 link and external backlink alive while consolidating the ranking signal
on one URL. Every builder falls back to the numeric id when the map is empty (DDragon outage, a champion
released between DDragon updates): that link still reaches the page and redirects, where
`/champions/undefined` would not.
One asymmetry is worth stating, because the obvious reading of "best-effort map" is wrong on half of it.
An empty map is cheap for *link building* — every builder falls back to the numeric id. It is not cheap for
*route resolution*: a slug has nothing to fall back to, so during a DDragon outage with a cold Nitro cache
`/champions/ahri` is indistinguishable from a typo. Answering 404 there would ask search engines to drop a
canonical, indexed URL over something transient and self-healing, so `championRouteAction` returns a
**503** while the map is empty and only 404s once the roster is actually known — #1124.

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
Three things make the cost small enough to accept. The fan-out sits behind a `defineCachedFunction`
keyed on (champion, lane, patch, rank), so it is **one backend call per slice per window, not one per
view** (5 min since #1273, an hour before it — see the entry below).
The endpoint resolves the ids to *names* server-side and returns ~1 kB, so the client never receives the
~373 KiB item map that made "just SSR the existing fetches" impossible in the first place. And it is keyed
on the **URL** filters rather than the reconciled `selectedPatch`/`selectedPosition`, which would flip the
key once the client-only aggregate lands and cost a second round-trip on every load.
Not a #149 regression, and the distinction is the load-bearing one: #149 was a *client-only* fetch racing
SSR and winning, so the server rendered content the client's first render didn't have. This fetch is
SSR-enabled and travels in the Nuxt payload, so hydration reads the same object the server rendered from —
the two agree by construction. Every interactive panel stays `server: false` exactly as before.
Accepted consequences: the summary trails the panels above it by up to one cache window — priced as an
hour here, cut to 5 min in #1273 when that hour turned out to be readable as a contradiction — and it
describes `builds[0]` — the tab the page opens on — never "the best build",
which would describe something the reader isn't looking at. It is **visible**, never `sr-only`: text
written for a crawler and hidden from the reader is cloaking — #1123.

**The build paragraph's cache window is 5 minutes, because it sits next to live numbers.**
#1123 priced the summary's staleness against the share card's: both were 1 h, and an unfurl an hour behind
the page it points at is nobody's contradiction. The paragraph's neighbours are not an unfurl. Every panel
on `/champions/{slug}` fetches client-side and live, so the two ages sit **side by side in one viewport**:
the patch picker (bound to the live `champion.patch`) read 16.17 over prose reading "on patch 16.16", and a
header reading "24 games · 66.7% WR" sat beside "Across 7 ranked games ... win 28.6%" — the same field of
the same endpoint, an hour apart. Early in a patch a sample can triple inside that window, which also
flickered the low-sample caveat on and off.
Keying on the *resolved* slice would fix the patch roll specifically — an unfiltered request keys on an
empty patch while its answer depends on the patch the backend picks, so the entry cannot notice the backend
moving on — but learning the resolved patch means making the very call the cache exists to avoid. So the
window shrinks instead: 5 min still collapses the page-view burst the cache was added for (#926 objected to
a backend hit *per view*, not per five minutes), and no visitor or crawler reads a dead patch number for
long — this paragraph is the only build content in the server-rendered HTML, so its staleness is indexed.
The share card keeps its hour: nothing renders beside it — #1273.

**`hydrate-on-visible` does not make a panel free — it defers hydration, not server rendering.**
The champion page's Truemains card is `<LazyChampionTruemains hydrate-on-visible>`, below the fold, and it
still fired `GET /api/truemains?championId=…` on **every** SSR of every champion page, because
`useTruemainsLeaderboard` defaults to `server: true` and the lazy wrapper has no say in that. So the budget
#1123 argued for — one SSR round-trip, cached an hour, spent on the build summary — was quietly double what
the entry above claims, and this second call had no Nitro cache at all. #1231 opts that one call site out
(`server: false`); the leaderboard skeleton becomes the server-rendered state, which is what a below-the-fold
panel should show anyway. The default stays `true`, because on `/truemains` and the homepage teaser the
leaderboard *is* the content. The general rule this leaves: a `Lazy*` + `hydrate-on-visible` wrapper is a
hydration-cost decision, and any fetch inside it is a separate, explicit SSR decision — #1231.

**The build paragraph is typeset, and rune trees get Riot's colours to do it.**
#1123 shipped the summary as flat grey prose under the build tabs, where it read as a wall of text naming
twenty entities a player normally reads as *pictures* — and, sitting below the panels, as a footnote to the
thing it describes. #1143 changed both. It moved into the right sidebar above Truemains, where it captions
the icon grid instead of trailing it, and each named entity now renders with its own 16 px icon inline and a
colour. That required one new token family, `--color-rune-*`: the five keystone trees in their in-client
colours. It is the second exception to "`--color-stat-*` is Riot vocabulary confined to tooltips", and it
earns the same justification `TIER_COLORS` does — a player reads "Domination" off the red before the word,
and a paragraph naming five runes from two trees is unreadable without it. The discipline is unchanged:
text emphasis only, never a fill, never on a measurement. Sorcery is a blue rather than the client's
blue-violet, the one deliberate departure, because violet is not a hue this app uses. Items take the
existing `--color-stat-gold`; summoner spells, abilities and the pinned opponent stay `text-highlighted` —
inventing three more hues to fill the table is the rainbow the palette exists to avoid.
Mechanically the sentences are built as **tokens**, not as strings a component re-parses: a regex over
finished prose would have to find "Doran's Ring" inside a sentence that also names "Doran's Blade", and the
builder already knows what each fragment is. `championBuildSentences` is those tokens concatenated, so the
paragraph a crawler reads and the one a reader sees are the same paragraph by construction rather than by
review. Em dashes are gone with the aside they set off — the build's share is its own sentence now, which
also means the share survives a build carrying no item data. Accepted consequences: the payload grew from
~1 kB to ~3 kB, 560 B to 844 B gzipped (one icon URL per named entity, and the shared CDN prefixes
compress away — still nothing next to the ~373 KiB item map it replaces),
and the block now sits after the main column in DOM order — deliberate, so a crawler and a keyboard both
meet the page's own build panels first — #1143.

**The champion link graph was server-rendered, then removed — the pages are back to zero internal champion links.**
#1123 gave the champion pages content a crawler could read. It did not give them links a crawler could
*follow*: counted in the HTML prod actually served, `/` held 0 `/champions/{slug}` anchors, `/champions` 0,
`/champions/tierlist` 0 and a champion page 0 — the only `/champions/*` anchor anywhere on the site was
`/champions/tierlist`. So the 174 build pages were reachable only from `sitemap.xml`, which on a site with no
backlinks is the textbook "Discovered – currently not indexed" profile, and it showed: `truemain katarina`
returned nothing, and `/` outranked `/champions` for `truemain champions`. #1209 fixed it with three blocks
of plain text links fed by `/api/champion-index` — deliberately *not* by SSR-ing the grids, which each need
the ~20 kB static champion list (names *and* CDN icon URLs) plus, on the directory, the ~373 KiB item map,
and whose rows are a `role="button"`, not an anchor, on purpose (#147).
**Reverted in #1275, on presentation grounds.** The link graph worked — 174 anchors on `/champions`, 173 on
the tier list, 12 on the homepage, no hydration message — but the blocks were a bare `flex-wrap` of 174
muted names pinned to the bottom of four pages, including every champion page, and no work had gone into
how they read. The product owner rejected them on sight. The endpoint, the composable, the pure assembly
helpers and their tests went with the components: an endpoint with no caller is worse than no endpoint.
What this costs, recorded so it does not quietly come back as a surprise: the SEO problem #1209 measured is
**open again**, and the champion pages are once more indexable-but-unlinked. #1209's technique is sound and
is the thing to reach for when it is retried — what needs solving first is the *presentation*, not the
plumbing: an A→Z index grouped under letter headings reads as a directory, a 174-item wrap reads as
boilerplate. Anything cheaper is worse, not better — `sr-only` links are cloaking (#1123), and the
contextual cross-links the matchups and synergies panels would give were already declined in #1209 because
they are backend reads and the champion page's SSR round-trip budget is spent on the build summary.
One piece of #1255 survives the revert because it was never part of the link graph: the homepage's ⌘K hint
stays `<ClientOnly>` — see the entry below — #1209, #1275.

**A platform-dependent `UKbd` cannot be server-rendered.**
The homepage's ⌘K hint (`<UKbd value="meta">`) resolves its modifier from the platform — `⌘` on macOS, `Ctrl`
everywhere else — which the server cannot know, so it rendered an empty key against the client's `Ctrl`:
"Hydration completed but contains mismatches" on every non-Mac visit to `/`, predating #1209 and found while
verifying its acceptance. It is `<ClientOnly>` now, with **no fallback** on purpose: the hint advertises a
shortcut that does not work until the handler is mounted, so showing it earlier is a promise the page cannot
keep — #1209.

**The paragraph's hover cards are resolved client-side, not carried in its payload.**
#1147 gave every mark in the build paragraph the tooltip its counterpart in the icon grid has. The obvious
implementation — put the tooltip body in the summary payload next to the name — is the one thing this block
must never do: the payload exists *because* naming an item server-side must not drag the ~373 KiB item map
into the HTML, and a hover card needs the whole record (stats, passive, gold), which is most of that map.
So the marks server-render their words and grow their cards at hydration, out of the maps the champion page
was already fetching for the panels. Cost to the page: nothing it wasn't already paying; cost to the
payload: nothing at all. The card is simply absent for the first paint, which is correct — a tooltip is not
content, it is a response to a pointer that does not exist yet.
Two things this pins. The tokens carry a `source` (`item` / `perk` / `perkStyle` / `summoner` / `ability` /
`champion`) rather than inferring the lookup from the tone: a Domination *rune* and the Domination *tree*
share a tone, and resolving one against the other's map yields no card at all — a failure invisible until
someone hovers the one word that has no tooltip. And the tooltip is rendered **disabled** until its map
lands rather than `v-if`-ed around the trigger, because Reka snapshots the trigger element in `onMounted`;
swapping it when the item map arrives is exactly the #1145 bug, which left tooltips able to open and unable
to close — #1147.

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

The rule covers anything else an immediate watcher can trigger, not just fetches: `useErrorToast` registers
its watcher under `import.meta.client`. Its `if (!value) return` guard made the SSR run a no-op only because
every error ref wired to it happens to come from a `server: false` fetch — true, unwritten, and untrue the
day one is pointed at the server-rendered build summary or the leaderboard, where a toast pushed during SSR
serialises into the payload and pops up unprompted for every visitor served that render — #1234.

**A closed `enabled` gate resolves `success` with an empty model, so the gated composables expose their own
`pending`.** `createChampionPatchSlice`, `useChampionTrend` and `useChampionPatchDiff` hold their request
until the champion's lane lands, and while held they resolve the empty read-model — which reaches
`status: "success"` with nothing loaded. A consumer driving a skeleton off `status` therefore renders its
"no data" state for the whole (client-only) champion fetch and only then fills in. Each composable now
returns `pending = gate closed || isLoadingStatus(status)`, deliberately superseding Nuxt's own `pending`
(which only knows about the request), so the trap is composed away once instead of re-documented at every
call site — #1234.

**Every hand-rolled fetch composable carries a monotonic request token.** `useCompositionBuild`,
`useCompositionBuildGames`, `useTruemainSearch` and now `useTruemainFetch` (profile / rank history /
activity / matches) all drop a response whose token is no longer the newest. Without it `useTruemainMatches`
— which refires on page, position and championId — can let a slow page-3 response land after page 4's and
write its rows under a pager reading 4 — #1234.

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
**Generalised (#1227): no migration may contain a statement Postgres refuses inside a transaction — at all.**
`migrationBuilder.Sql(..., suppressTransaction: true)` does not buy an escape hatch on the real deploy path.
That flag only has an effect for `Database.MigrateAsync()`, and `ApplyMigrationsOnStartup` is permanently
`false` in preprod and prod. Deploys run `dotnet ef migrations script --idempotent` piped into
`psql --single-transaction`, which defeats it twice over: `--idempotent` wraps *every* statement in a
`DO $EF$ ... END $EF$` PL/pgSQL block (Postgres: `CREATE INDEX CONCURRENTLY cannot be executed from a
function`), and `psql` opens an explicit transaction around the whole file (so a procedural `COMMIT;` inside a
`DO` block is an `invalid transaction termination`). Six migrations carried one or the other. It was silent
because preprod and prod already had them in `__EFMigrationsHistory` and the idempotent guards skipped their
blocks — it would only have surfaced on the first database built from scratch: a new preprod, a DR restore,
onboarding. All six were rewritten as plain transactional DDL, which is free: their bodies now only ever
re-execute on an empty database. The `migrate-fresh` CI job applies the generated script to a blank Postgres
on every PR so this cannot come back.

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

**An incomplete prod deployment configuration fails the release run; it is never a green skip.**
Because migrations apply *before* the images roll, checking the deploy-side configuration at deploy time
could only ever produce the mismatch in the other direction: an empty `PROD_ENV_FILE` or an unset
`HOSTINGER_PROD_VM_ID` skipped the image roll — green — after `migrate-prod` had already moved the schema,
leaving prod on the old binary against the new one. All of it (SSH secrets, `PROD_ENV_FILE`,
`HOSTINGER_PROD_API_KEY`, `HOSTINGER_PROD_VM_ID`) is now checked by a `preflight` job that every other job
depends on, `publish` included — publishing would otherwise move `:latest` ahead of what prod runs. Both
halves of a release happen or neither does — #1228.

**Both deploy pipelines serialise at workflow level, not per job.**
Preprod needs it because the `-rc.N` counter is read from the remote tags; prod needs it because two
releases published back to back would interleave their `publish` jobs and race for the moving `:latest`
tag. `cancel-in-progress: false` in both: a running deploy finishes, and GitHub collapses the pending
queue to the newest run — a visibly cancelled run, never a half-deploy — #1228.

**Integration tests run on pushes to `develop`/`master`, not only on pull requests.**
The push to `develop` is the commit that deploys to preprod and the develop→master merge is the one a
release is cut from — the two trees whose behaviour is about to hit a real environment were the only ones
skipping the Testcontainers suite, and a squash merge produces a tree no PR run ever tested. The cost is a
few Testcontainers minutes per merge — #1228.

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
  with `npx npm@11.13.0` (older npm omits sharp optional deps), the version CI pins for the frontend jobs
  since #1236 — before that, CI ran whatever npm the resolved Node 24 build shipped, so the lock file's
  generator and the installer could silently diverge.
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
- **A tooltip trigger keeps the same DOM element for the life of the component.** Reka reads the trigger node
  once, in `onMounted`, and binds the hoverable-content "grace area" `pointerleave` to that snapshot. A trigger
  that swaps its root element later — a `v-if` icon / `v-else` fallback box flipping over when the static data
  lands — leaves that listener on a detached node, so the tooltip opens normally and can then *never* close on
  pointer exit: sweeping across a row of icons piled every tooltip it touched on screen. Icon components that
  can render before their data therefore keep one unconditional root (`SkeletonImage`, which draws the text
  fallback itself via its `fallback` prop) instead of two branches. Item and perk icons never had the bug —
  they always rendered a single `SkeletonImage` — which is why the symptom looked specific to skill orders and
  summoners.
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

- **A skeleton is the real component in `pending` mode, not a drawing of it.** The champion page's build
  section has two loading phases it cannot merge: the aggregate and the patch-pinned static bundles are
  separate fetches, and the ~95 DDragon icons only start downloading once the ids they resolve are mounted.
  So the reader sees *a* placeholder while the API answers, then the real panels with every icon still pulsing
  while the images land. `ChampionBuildTabsSkeleton` used to be a hand-drawn stack of grey blocks sized to the
  measured real heights: it reserved the space, but it was a second, unrelated picture, so a cold load visibly
  rebuilt itself the moment the API answered. It now renders `ChampionBuildTabs` itself over a placeholder
  aggregate (`app/utils/build-placeholder.ts`) with `pending` set — unresolvable ids, so every icon falls back
  to the same pulsing box `SkeletonImage` already draws mid-load, and every number is masked (`RateBadge`,
  the tab pickrate) rather than printing the placeholder's filler figures. The two phases become one
  continuous state whose only transition is the content filling in, the skeleton cannot drift when a section
  moves, and CLS is exact instead of estimated. `pages/dev/build-skeleton.vue` renders both skeletons with
  nothing to fetch, the same way `dev/match-row.vue` makes a row reviewable in isolation. The escape hatch
  is per-section: the skeleton takes `powerspikes` because the player-scoped page's tabs carry no population
  scope and its real card has no such section to reserve.

- **Icon slots are rendered from the ids, never gated on a resolved static lookup.** Same rule as the
  tooltip-trigger one above, from the other side. The build tabs' leading item/keystone icons were gated on
  `itemsMap[id]` / `runeTree.perks[id]`, so the whole tab bar reflowed when those deferred (~370 KiB, patch-
  pinned) payloads landed — and swapping the trigger element that late is exactly the case that leaves a Reka
  tooltip unable to close. The id is what answers "is there something here"; `SkeletonImage` already draws the
  loading box for a null icon. `itemSlots()` in `shared/utils/build.ts` exists for the same reason.

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
- **The top of the axis has a second step — written down in #1237, months after it shipped.** `--color-gold`
  sits above `--color-data-good` for a *standout* value: a Perfect KDA, a 75+ performance score
  (`MatchRow.vue`). It arrived with the match history and was never documented, so `DESIGN_SYSTEM.md`,
  `main.css` and this entry all described a two-tone axis the code had not had for months. The call was to
  document the step rather than retire it — the grading is right, and a three-tone read is what an op.gg-style
  row needs — and to leave it on `--color-gold` rather than mint a `--color-data-standout`: it is the same
  token the MVP crown wears, and that identity *is* the point, since the number and the accolade are saying
  the same thing. A second name for one hex is how those two drift apart. **"One-sided" is therefore a claim
  about the bottom of the axis**: there is still no opposed hue for "bad". The standout step is the one member
  of the axis that is text and small marks only — a gold fill would out-shout the accent it exists to cap.

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
SFC-mounting test setup: every colour family, elevation step and material on one screen, stripped from
production builds like the other `dev/*` playgrounds.

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

## Region balance is a target, not a quota: coverage deficit allocates every budget (2026-08-19)

Match ingestion had settled at roughly **82% EUW1 / 14% NA1 / 4% KR** — the same split in stored matches,
tracked accounts, active mains and Riot calls. Nothing configured that split; it was the fixed point of a loop
with nothing opposing it. Every champion cleared `Coverage:TargetMainsPerChampion` on EUW1 while ~79% of
champions sat below it on KR, so champion pages presented a near-single-region sample as a global stat.

Three mechanisms compounded, and all three were "one global ordering over a pool that the previous run had
just fed":

- **Ladder discovery — the only per-platform-budgeted source — had been dead since 2026-06-14** (#1149): its
  cadence guard recorded each skip as a completed run, so the skip re-armed itself forever. That left the
  participant harvest as the sole feeder of new accounts.
- **The harvest can only reproduce the mix that produced it.** Its eligible pool is orphan
  `match_participants`, i.e. the matches we already ingested, so the densest observations are always the
  region we ingest most; ordering the union by observed games handed it the budget. Note `droppedNew` was
  **0** — the budget was never the bottleneck, the eligible pool was, which is why raising
  `Harvest:MaxCandidatesPerRun` would have changed nothing.
- **The claim ordered cross-platform by `LastMatchIngestAtUtc`, nulls first.** Right priority inside a region,
  fatal across regions: the region creating the most new accounts automatically captured most of the batch.

**The fix is not a configured per-region match quota.** "300 EUW, 300 KR" would be a guess, would need
re-tuning whenever a region is added, and would keep spending on a region that no longer needs it. Instead the
share follows the signal the pipeline already computes for champions:

- **Coverage is now keyed on (platform, champion)**, not champion alone (`GetMainCountsByPlatformAndChampionAsync`,
  `IX_main_champion_stats_is_main_champion` re-keyed to `(PlatformId, ChampionId)`). A champion-only count is
  dominated by whichever region we ingest most, so the one signal that could have damped the imbalance was
  blind to it: 60 EUW1 mains and 1 KR main read as *covered*, and every under-served region got a zero
  scoring bonus and no threshold relaxation.
- **`PlatformBudgetAllocator` splits every budget** — the claim batch and the harvest budget — by
  `weight(p) = 1 + MeanDeficit(p)`, apportioned largest-remainder. `MeanDeficit` averages over the *shared*
  champion universe, so a region missing a champion entirely is charged the full deficit for it rather than
  scoring as perfectly covered.
- **The constant `1` is what makes this a balancer rather than a switch.** A fully covered platform keeps its
  even share and its established mains keep being refreshed; the deficit is a bonus on top, capped at 2× a
  covered platform's share. Starving the leader would only invert the imbalance.
- **Self-damping, like the per-champion signal.** As a region fills, its deficit shrinks, its weight decays
  toward 1, and the allocation converges on an even split instead of oscillating.
- **Quotas are floors, not partitions** — the same semantics as `Harvest:NewCandidateShare` (#495) and
  `MatchIngestion:EstablishedMainShare` (#900). A platform that cannot fill its slice releases it, and the
  spill is **round-robin**: handing the whole remainder to whichever platform sorts first is the
  cross-platform ordering again, and would quietly restore what the quotas exist to correct.
- **Nulls-first survives, scoped per platform.** Never-ingested accounts still go first *within* a region; it
  was only ever harmful across them.

Observability is deliberately not part of this: the per-platform balance is visible in the claim's allocation
log line and the `HarvestBudgetExhausted` event, but the admin portal has no region-balance panel yet
(tracked separately). Until it does, a drift like this still has to be inferred from run summaries.

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

## Jungle first-clear tracking was built, then removed entirely (2026-08-24)

Shipped over #1186–#1195, then deleted: the camp sequence, the storage, the ingestion, the match-detail tab
and the Core camp geometry. The product call is that a first clear which cannot be tracked *completely* is not
worth its surface, and complete tracking is not achievable from Riot's data.

Why it cannot be: `participantFrames` are sampled **once per minute** while a clear runs from the 1:30 spawn to
about 3:15, so the whole clear is covered by two usable samples, and Riot emits **no camp-kill event** (the old
sub-elite buff kills were removed deliberately). Six camps cannot be ordered from two positions. The original
#535 builder credited one camp per frame, which put a hard 6:00 floor under a clear that really ends near 3:15 —
of 148 237 production rows only 84 (0.06 %) ever reached six camps.

The full account, including the measurements, the three bugs found on the way and the two contradictions that
were never resolved, is in **issue #1206**. Read it before proposing this again.

Two general rules the episode paid for, and they outlive the feature:

- **Check a rate a spec asserts before building on it.** "~1 camp/min" was never true; everything downstream
  inherited it. The sampling interval bounds what is knowable, and no inference recovers detail the sampler
  never captured.
- **Do not store what you can derive.** The full-clear time was stored *and* derivable; when the rule behind it
  changed, the derived side healed and 35 % of the stored side silently did not.

## Preprod builds carry a prerelease version, tagged only after they deploy (2026-08-24)

Every push to develop reaches preprod a few minutes later, but nothing on the page said *which* build was
serving it — "is my change on preprod yet?" and "did this reach prod?" were answerable only by comparing SHAs
on GitHub. `deploy-preprod.yml` now resolves `<base>-rc.<N>` and the footer prints it (`preprod · 1.20.0-rc.4`);
prod prints the bare release tag it is serving.

Three calls worth keeping:

- **A prerelease, not build metadata.** `1.19.0+7` (commits since the last release, derivable with a single
  `git describe` and no tags at all) was the cheaper design and was rejected: the same string tags the four
  images on GHCR, and `+` is **illegal in a Docker reference**. `-rc.N` is legal, so the version can be one
  string everywhere — footer, git tag, image tag.
- **The tag is pushed after the deploy succeeds, not at build time.** The tag's whole job is to mean "this ran
  on preprod". Tagging in the publish job would mint tags for commits that never got there, recreating the
  ambiguity it replaces. Cost: a build that fails after publishing leaves a gap in the sequence, which is the
  honest reading.
- **The base is a label, not a promise.** It defaults to the next *minor* after the latest release, because
  that is what a plain "release" cuts — but the real bump is still decided by the user's word at release time
  (see the `release` skill), so a `1.20.0-rc.*` line can perfectly well ship as `1.19.1`. Set the
  `PREPROD_VERSION_BASE` repo variable when the next one is known to be a major.

The trap this introduces, and it bit during implementation: **git's version sort ranks `1.20.0-rc.4` above
`1.20.0`**. Anything reading "the latest version" must filter to bare `MAJOR.MINOR.PATCH` or it will read a
preprod build as the last release and skip a version on the next bump. The `release` skill was updated for
exactly this.

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

## A chart's mark is chosen by what the series measures: flows are bars, stocks are lines (2026-08-25)

The candidate funnel's `validated` series read as a dead flat line near the axis and was taken for a broken
counter. It was moving ~350 accounts a day. Two independent causes, and fixing only one would have left the
chart just as misleading.

**The mark contradicted the metric.** `scored` / `promoted` / `validated` are per-bucket *flows* — how many
candidates moved during that run period — but they were drawn as lines. A line asserts a level that rose and
fell and invites reading its height as a state; a steady flow therefore renders as a flat line saying
"nothing is happening", which is the opposite of what a steady flow means. The rule now written into
`admin/app/utils/charts.ts` and applied across the portal: **a flow (counted per period) gets vertical bars;
a stock (a level at an instant, or a running total) gets a line or area; a categorical top-N gets horizontal
bars.** `/database` carries the contrast in one page — disk size stays an area because it is the level the
volume sits at, while rows-added-per-day became bars because it is that day's delta.

**And the scale hid it regardless of the mark.** 10,593 validated against 147,290 scored on one linear axis is
squashed onto the baseline whatever shape you draw. So Progression was split: `scored` + `promoted` stay
together as *grouped* bars (they are the competitive top-N cut, comparable magnitudes, and stacking them would
count the same candidate twice since promoted ⊂ scored), and `validated` + `demoted` moved to their own chart
as **cumulative** curves. That is not an exception to the rule — a running total is a stock, and "how many
accounts have we validated" is a roster size, so a line is the correct mark for it. It also happens to be the
view that was asked for. The accumulation restarts at the left edge of the selected window, which the caption
states so the endpoint is not read as an all-time count; periods before `validatedFirstMeasuredAtUtc` stay
absent from the curve rather than accumulating as zeros (#924).

The split doubles as palette hygiene. `CHART_SERIES` holds three colours in a fixed order chosen for
colourblind separation, and the file already said a fourth series "gets its own chart" — Progression was at
three and the outcome series had nowhere to go.

Grouped, not stacked, is a recurring call and it turns on nesting: stack only when the series sum to a real
whole. The funnel's three intake sources do (they add up to "candidates that entered"). `promoted ⊂ scored` and
`retries ⊂ calls` do not, so `/riot-api`'s call-volume chart is grouped bars — that one was safe to convert
because its window fixes the bucket size server-side to land 12–28 buckets, wide enough to read as bars at
every window.

## Admin bar charts go through a wrapper, because vue-chrts' bar tooltip is broken twice (2026-08-25)

Hovering a bar on `/candidates` opened an empty white box. Reproduced in a headless browser against
`vue-chrts` 2.1.4 — and the same code is still in 2.2.1, so the upgrade was checked and is not the fix. Two
independent upstream defects, which is why fixing one still left an empty tooltip.

**A stacked bar chart never shows values.** `@unovis/ts` binds each stacked bar to a wrapper —
`{ datum, index, stacked, stackIndex, isEnding }` — while `vue-chrts` looks the category keys up on that
wrapper's *root* and excludes only the wrapper's *old* key names (`_index` / `_stacked` / `_ending`). Nothing
matches, so it renders neither title nor rows. Grouped and horizontal bars bind the row itself; all three
shapes were tested side by side before concluding, which is what localised the bug to stacking.

**And the first hover on any bar chart shows an empty box.** The tooltip trigger mutates a Vue ref and then
reads a hidden `<div>`'s `innerHTML` in the *same tick*, one frame before Vue flushes. A second mousemove over
the same bar fixes it, which is what made the bug look intermittent rather than systematic. This one predates
the bar conversion (#1218) — it already affected the horizontal bar charts on `/champions`, `/database`,
`/riot-api` and `/`.

Both are repaired in `admin/app/components/charts/BarChart.vue` (`<ChartsBarChart>`), which every admin bar
chart now goes through: it renders the tooltip via the `#tooltip` slot — body in the sibling
`BarChartTooltip.vue`, so the hovered row and its series resolve once per render as computeds instead of being
recomputed by every expression that needs them — and replays one mousemove on the next frame. The replay listens in the **capture** phase — the upstream handler calls
`stopPropagation()` as soon as a trigger matches, so a bubbling listener never runs at all. The markup mirrors
upstream's own inline styles and CSS variables, because this is a repair and not a restyle: a bar tooltip and
an area tooltip must stay identical to look at.

The trap to remember when touching it: `@unovis/ts` maps the **value** to the bottom axis for horizontal bars,
so a horizontal chart's `yFormatter` is its index → label lookup, not its value formatter. The wrapper makes
the same swap upstream does. Get it wrong and the tooltip prints a bucket label where a count belongs — which
typechecks, renders, and is wrong. That case is pinned by a test.

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
across regions and across renames — which is why that index is deliberately not unique, see the entry above —
so the **most recently active row wins**, `Id` breaking an exact timestamp tie. Locked by
`TruemainAccountResolutionApiIntegrationTests`, which walks several routes per casing.

The lookup is `lower("GameName") = $1 AND lower("TagLine") = $2` rather than `ILIKE`: equality sidesteps LIKE
metacharacters in raw user input entirely, and it is the exact expression a functional index on
`(lower("GameName"), lower("TagLine"))` would serve — which an `ILIKE` could not use. **No such index exists
yet**, so this trades the index seek the nine `==` copies got for a sequential scan of `riot_accounts`. That
was accepted knowingly for this PR: adding one is a schema change (compiled-model regeneration, migration) and
belongs in its own, measured PR.

## The admin's tracked-region list stays a checked-in constant, not a read of `/ops/configuration` (2026-08-28)

`KR / EUW1 / NA1` was written four times: the Ingestor's `Platforms:Active`, the API's `TrackedPlatforms`
guard in `SeedRequestService`, the admin's `REGION_ITEMS` filter options, and a second admin copy in
`seed.vue` whose bulk parser rejected anything outside it with `Unknown region "…"`. Adding a shard therefore
meant editing the pipeline config *and* both admin copies, or the add form would refuse a region the pipeline
was already crawling.

The two admin copies collapse into `admin/shared/utils/regions.ts` (#1249). Deriving them at runtime from
`GET /ops/configuration` — which does expose the effective `Platforms` section, and which the admin
`/configuration` page already renders — was considered and **rejected**:

- these values populate `<USelect>` options that must exist before any ops call resolves; one slow or failed
  request would leave the seed form's region select empty, i.e. unusable, to save a constant;
- the list is also a **type**. `TrackedRegion` constrains the bulk parser's `defaultRegion` and every parsed
  row at compile time; an array fetched at runtime cannot, so the parser would lose a guarantee to gain a
  dependency;
- the config array's order (`KR, EUW1, NA1`) is a deployment detail, not a UI ordering, and binding the
  selects to it makes an unrelated config edit reshuffle the admin.

The Ingestor config remains the source of truth for what the pipeline crawls; the admin holds one declared
copy of it, with `/configuration` displaying the effective values next to it so a drift is visible rather than
silent. The API-side `TrackedPlatforms` set (a log-warning guard only — an untracked platform is still
accepted) is deliberately left alone: sharing it would mean wiring Ingestor configuration into the API, which
the two projects otherwise never do.

The same change does **not** fold `web/shared/utils/region.ts` into any of this, despite the audit grouping
them. That file maps ten platform ids onto the three public `europe/americas/korea` slugs and mirrors the
backend `RegionFilterParser` — the *exposed* set, which is wider than and independent of the *tracked* set.
Treating the two as one list would be the real bug.

## `web/` and `admin/` duplicate their Data Dragon helpers on purpose, and the copies are labelled (2026-08-26)

The two apps are deliberately separate — different auth, different rendering mode (`ssr: false` in the admin),
different deploy — and there is no shared package to hold common code. That is not changing: a package would
couple two release cadences to save a few dozen lines. But two files *were* copied between them and then
drifted **in both directions**, which is the failure mode worth guarding against, not the duplication itself.

By the time it was caught (#1226), `server/api/static/champions.get.ts` existed twice with each copy carrying a
fix the other was missing. The admin had re-inlined an **uncached** `resolveLatestPatch()`, undoing #947 — and
worse there than on the web, because the admin renders client-side, so that DDragon round trip ran once per
page load rather than once per SSR. Meanwhile the admin had added a `?patch=` format guard that the web — the
only *public* app — never received. `shared/utils/ddragon.ts` had drifted too: same code, comments edited
independently on each side, and the #966 alternate-mode floor pinned by a test on the web side only.

The rule that came out of it: a file duplicated across the two apps **says so in a header naming its twin**, and
the behaviour it encodes is pinned by a test in *both* suites. Labelled copies are `shared/utils/ddragon.ts`,
`server/utils/ddragon-patch.ts` and `server/api/static/champions.get.ts`; the champion handlers differ only by
the admin's `requireUserSession` gate, so any other difference in a diff is a regression, not a variant.

`PATCH_PATTERN` (`^\d+\.\d+\.\d+$`) sits next to `normalizeDataDragonPatch`, which produces the value it
validates — that function expands the short `16.5` form the backend scopes expose and passes everything else
through untouched, so it is a shape fixer and never a guard. Every static endpoint interpolates the result into
a CDN URL *and* uses it as a cache key, so an unvalidated `?patch=` is both a path-injection vector and an
unbounded-cache-key vector: one entry per distinct string, held for the payload TTL. The guard lives in
`normalizeRequestedPatch` and covers all four web static endpoints, not just the champion list.

## The admin portal has one status vocabulary and one duration ladder (2026-08-28)

Six presentation rules were coded twice or more across the portal, and two had already drifted into
readings that contradicted each other. The failure mode is always the same, so the rule is now that
`shared/utils/` — or `app/utils/` for the client-only ones — owns each of these outright.

**A run status is `info` while it is running, never the emerald `primary`.** `/processes` painted from its
own private table and `/health` from `pipeline-health.ts`, so the same in-flight run was emerald on one page
and blue on the other. `info` wins: emerald is this portal's "this succeeded" colour, and a run still going
has not succeeded yet. Colour *and* icon now live together in `PROCESS_STATUS_META`, the way
`DETECTOR_STATUS_META` already did — and the chain view's `notRun` is an adapter onto the cockpit's
`Missing`, because "there is no run to report" is one claim, not two.

**A duration is humanised in exactly one place, and it counts days.** `formatDuration` stopped at hours
while `formatGapMagnitude` had a private ladder that reached days, so a three-day span read `72h` on
`/processes` and `3d` on `/health` — two pages that link to each other. There is now a single
`formatElapsed(ms)` carrying the days tier, which `formatGapMagnitude` delegates to, keeping only the two
things genuinely local to a gap: its "not measurable" wording and its minutes-to-ms conversion.

It is also renamed. `formatDuration` meant humanised milliseconds in the admin and a `mm:ss` game clock in
`web/app/utils/relativeTime.ts` — one name, two contracts, one repo. A copy-paste between the two apps
produced a wrong display that no type error caught.

**A percentage is `formatPercent` / `formatPercentOrDash`, and an absent share is a dash.** Five call sites
each formatted their own, and `/riot-api` printed `0%` for a status-code share when the window had counted
no calls at all — a fabricated number, since `0%` claims "this status never happened" and no reading
supported it. The share is now `null` when there is no denominator and renders as the em dash, the same rule
the API side has followed since #924/#1024. Only the *bar* beside it still resolves to a zero width: it is a
drawing of the share, not a reading of it.

The one duplicate deliberately left standing is `DataQualityDetectorItem.formatLevel`. It looks like the
others and is not: it also accepts an already-scaled `percent` unit, and it trims trailing zeros so a
configured threshold reads `40%` rather than `40.0%`. Routing it through `formatPercent` would regress the
display to buy a shared call.

## Configuration defaults live in the class, and the two champion games floors are two keys

**`appsettings.json` only carries what differs from the class default.** The ingestor's file used to restate
~30 keys that were already the default of their `*Options` class, which made `/configuration` (#1034) useless:
every one of them was tagged *override* though nothing was overridden, and that noise hid the single key that
genuinely diverged — `Discovery:MaxAccountsPerPlatformPerRun`, 500 in JSON against a class default of 350, so
for months nobody could say which value was in force. It was 500. That value moved onto `DiscoveryOptions`
(both deployed stacks override it anyway: 750 prod, 100 preprod) and the JSON key is gone.
`IngestorAppSettingsNoDefaultsTests` now fails the build if any key comes back equal to its class default; the
documentation-only empty sections (`Riot`, `CommunityDragon`, `Job`) and the list-valued keys stay, because a
list default deliberately lives in JSON — the binder *appends* to a non-empty list instead of replacing it
(#860).

**`ChampionsList:MinSampleGames` (10) and `ChampionsList:MinBuildSampleGames` (20) are different questions.**
The first decides whether a `(champion, lane)` line is listed and ranked at all; the second decides whether an
item/rune distribution *inside* a line is a usable sample — it splits its games across several builds, so it
needs more of them. The build floor used to be two hard-coded `20`s, in `ChampionBuildsQueryService` and
`PlayerBuildDivergenceQueryService`, each documented as a mirror of the other with no code link between them,
while an operator reading `MinSampleGames = 10` on `/configuration` or `/patch-coverage` would infer the wrong
bar for the build panel. Both now read the new key, and the whole `ChampionsList` section is on the
configuration page. The existing key was **not** renamed: a config-facing section rename breaks deployment
(#889).

**When a process needs candidate writes, it goes through `IDataSession`.** `MatchDataRetentionProcess` used to
`new` a `MainCandidateRepository` over its own `DbContext` — the only hand-built repository in the ingestor —
although the purge it wanted is already on `IDataSession.MainCandidates`. `IDbContextFactory` stays the right
tool for the set-based deletion passes in the same file, which are raw `ExecuteDelete` work over a scoped
context; a repository operation is reached through the session.

## Tables are snake_case, columns are PascalCase — and enums split by who reads them (2026-08-28)

**Written down because a review read the table names and inferred the wrong rule** (#1251). "Postgres schema,
therefore snake_case everywhere" is a reasonable guess and it is wrong here: every table is snake_case
(`champion_matchup_stats`, `riot_accounts`), and every column is quoted **PascalCase** (`"ChampionId"`,
`"IsMain"`, `"PowerspikeAggregated"` — see the raw SQL filters in `MatchConfiguration` and
`Data/DataQuality/ChampionDimensionCanonicalKeys.cs`). There is exactly one exception, `elo_bracket`, mapped by
hand with `HasColumnName` in seven configurations. Applied literally, the "snake_case columns" reading would
have produced new snake_case columns in the middle of a PascalCase schema — making the mix worse in the name
of fixing it.

**Nothing is renamed.** Aligning either side is a heavy migration over the largest frozen tables in the
database and buys nothing a reader cannot get from one sentence. What is guarded is the drift:
`SchemaNamingConventionTests` fails when a new table is not snake_case, or a new column is neither PascalCase
nor the allow-listed `elo_bracket`. Do not extend that allow-list — an entry there is the inconsistency
spreading, which is the only outcome this decision exists to prevent.

**Enum persistence follows who reads the column, and that was previously unwritten.** Three shapes coexisted,
each justified locally and none globally: `SeedRequestConfiguration` stores text (`HasConversion<string>`,
"readable in ad-hoc SQL"), `MainCandidateConfiguration` and `RiotAccountConfiguration` store ints — which is
why the partial index on `riot_accounts` has to spell `"MatchIngestStatus" <> 0` in raw SQL, with a comment
apologising for it — and `ProcessRunDocument` uses `BsonType.String` on the Mongo side. The rule, for **new**
enums:

- **A lifecycle state an operator reads or writes by hand goes to text.** Seed request status, anything an
  admin panel exposes, anything that turns up in an incident's psql session. The width is irrelevant next to
  a query that says what it means.
- **An internal flow flag stays an int.** Claim/lease states, fold progress markers — columns only code
  touches, sitting in hot partial indexes, whose set of values changes with the code that reads them.
- **Mongo documents always store enums as strings** (`BsonType.String`). Those collections are read ad hoc by
  definition, and a Mongo document has no migration to rescue a renumbering.

No retroactive migration: the existing three stay as they are. This settles which one a new column copies.

## A unit of work covers the writes and nothing else (2026-08-28)

**Transactions wrap writes, never Riot calls.** `MatchIngestionProcess` used to open its per-account
transaction *before* up to 40 Riot round-trips (20 match-v5 + 20 timelines), each able to burn the client's
whole `EffectiveTotalRequestTimeout` under a 429 backoff. The connection sat `idle in transaction` for minutes
per account, holding the claim locks and pinning VACUUM's horizon — the exact counter-model of #264, which had
already removed that pattern from MainAnalysis. Ingestion now runs in two phases: a fetch phase that
materialises the DTOs (bounded by `MatchIngestion:MatchesPerAccount`, so a few MB per account), then a
transaction around the writes only. The property the transaction was opened for is unchanged: a crash still
cannot leave a partially ingested match, and a crash during the fetch phase writes nothing at all — the replay
is idempotent through `GetExistingMatchIdsAsync` and the `TimelineIngested` flag (#1229).

**A trailing `SaveChangesAsync` after `ExecuteUpdate` calls is not a commit point.** `ExecuteUpdate` /
`ExecuteDelete` never enter the change tracker, so the save commits nothing and only makes the code *look*
atomic. `AccountValidationService` chained three of them bare: a failure between the first and the last left
candidates `Validated` while the account stayed `Processing` for a whole claim lease. All three of its exit
paths — `ValidateAsync`, `RevertAsync` and `ReleaseUningestableAsync` — now run inside an explicit
transaction, and the decorative saves are gone. The release path is the one where a partial failure hurts
most: the account would keep its place at the head of the claim ordering and be re-claimed at once, only to
prove uningestable again (#1229).

**`ChangeTracker.Clear()` is safe only when the batch owns everything it loaded.** The ingestor's long loops
(Scoring, Discovery, AccountRefresh, participant harvest) drain the tracker after each batch save via
`IDataSession.ClearTracking()` — without it every `SaveChanges` re-runs `DetectChanges` over every entity the
run has ever touched, which is quadratic in the number of batches. The catch: three of those loops preloaded
tracked entities for the *whole* run and mutated them later (rank snapshots overwritten in place, harvested
candidates re-scored). Clearing under a run-wide preload detaches them, and a detached entity accepts property
writes and persists none — silent data loss, not an error. So each preload moved inside its batch. Don't
"optimise" one back out to the top of the loop: the three loops that carry the risk (Discovery,
AccountRefresh, the participant harvest) each have an integration test that runs more than one slice and
fails if the preload is hoisted — a unit test cannot catch this, since a mocked `IDataSession` makes
`ClearTracking()` a no-op (#1229).

**The Ingestor's file heartbeat is liveness, not progress.** It used to be touched once per loop iteration, so
it went stale for a whole `Job:IntervalMinutes` (60 min) plus a whole `Full` pass; the healthcheck had to
tolerate 6 h of silence, which left it unable to detect anything short of a process dead for a quarter of a
day. A dedicated 30 s loop now refreshes it for the worker's whole lifetime and the threshold is 300 s, so a
wedged process is caught in minutes. Whether the *work* is progressing is a separate question with a separate
answer already in place: the `process_runs` heartbeat, which ages a stalled run out to `Abandoned` (#1229).

## Ranks are read from the ladder, not from one account at a time (2026-08-30)

**The ladder endpoints are the primary rank source; the per-account call is the fallback.** `AccountRefresh`
spends one `league-v4/entries/by-puuid` call per account at `BatchSize: 200` a cycle, which caps the whole
fleet at roughly 2 400–4 800 refreshes a day — and that budget is shared with the Riot-ID identity backlog
(#788), whose P0/P0.5 buckets take the entire batch whenever it is large. The visible result was LP that was
days stale. The ladder answers the inverted question far more cheaply: one call returns a whole apex tier, one
paginated call returns ~205 consecutive players of a division, and matching those entries against accounts we
already store is a pure SQL join. `LadderSyncProcess` reads the three apex ladders every cycle (nine calls for
three platforms) and sweeps the tiers below Master incrementally — #1312.

**Sweep depth is bounded by a request budget, not by a tier list.** A full Challenger→Emerald pass over three
platforms is on the order of 3 900 calls (Emerald alone is ~1 100 pages per platform). `MaxRequestsPerRun`
therefore buys sweep *rate*, not sweep *coverage*: the cursor resumes where the previous run stopped, so
configuring a deeper scope costs latency to come round again, never a budget blowout. The rentability rule per
division is `tracked_accounts > population / page_size` — roughly **0.5 % of the division**. Master+ clears it
by a wide margin, Diamond very likely, Emerald is the uncertain step, which is why the run summary reports
entries and matches per tier: the scope is a measurement, not a guess.

**The sweep never inserts accounts.** Seeding every player of every swept division across three regions would
add millions of `riot_accounts` rows and swamp every downstream step. Discovery stays the only intake from the
ladder, and stays scoped to the apex tiers.

**Platforms rotate one page at a time, and the cursor advances before the fetch.** Draining one platform
before the next would mean the last platform never advances once the budget is the binding constraint — the
same region-blind allocation as #1149/#1150. Advancing the cursor first is the #486 lesson: a page that fails
deterministically must not pin the sweep on it forever.

**An account that leaves the swept range needs no special case.** It is simply not seen, so
`LastRankSyncAtUtc` does not advance and `AccountRefresh` picks it up in its normal rotation — which is also
what re-detects a demotion. `AccountRefresh:RankSyncFreshness` moved from 15 minutes to 12 hours in the same
change: at 15 minutes the gate expired long before the next sweep came round, the per-account call was
re-issued anyway, and none of the saved budget was actually reallocated.

## The sitemap advertises champions, not players (2026-09-01)

**Player profiles are not in the sitemap, because their server-rendered document is empty.**
`/truemains/{nameTag}` fetches its profile client-only (`useTruemainFetch`, the #862 decision that keeps SSR
from cross-pollinating viewers), so what a crawler receives on the first pass is a skeleton: 685
`animate-pulse` elements, the same generic `TrueMain player profile.` description on every page, and the
player's name appearing exactly once, in the title. Advertising 5,000 of those hands Google 5,000
near-duplicate empty documents on a domain it knows 5 URLs of, and buries the 174 champion pages — the only
fully server-rendered content, and the only content this site can realistically rank on. They stay reachable:
`/truemains` is in the sitemap and links profiles, so a crawler can descend if it judges them worth it. A
sitemap is a priority signal, not an access gate — #1337.

**The family was specified in #551 and never once worked, so nothing was withdrawn from Google.** #551's own
verification note reads "Truemain profile URLs populate when the backend is running; it was off locally, so
that list was empty"; the page-size bug fixed in #1336 then kept the list empty in production too. Reopen the
question only if the profile page starts rendering its content server-side, not because the code once
intended to enumerate it.

**A route family that contributes no URLs warns.** Each family is fetched defensively so one upstream outage
cannot fail the whole sitemap — that part is right, and stays. What was wrong was that an empty family still
produced a valid, well-formed sitemap and said nothing, which is why the missing profiles sat in production
from the SEO foundation until someone counted the URLs — #1334.

**If a dynamic slug family ever returns: the `loc` carries the raw value, and @nuxtjs/sitemap owns the
encoding.** The app's own `to`/`href` builders correctly `encodeURIComponent` a nameTag; copying that into the
sitemap source encodes it twice, and `Álec Lightwood-Jace` gets advertised as `%25C3%2581lec%2520Lightwood-Jace`,
which the route hands to the backend as literal text — a 404. Riot IDs are full Unicode, so this hit 2,334 of
the first 5,000 profiles before the family was dropped. Encoding is per-consumer, and a `loc` is not an href.

## A lease is only kept if something reaps it (2026-09-01)

**`Processing` is a lease state, and the pipeline now enforces the lease.** `MatchClaimService` moves an
account's candidates `Queued -> Processing` and stamps the account's claim; every ordinary exit path settles
them again. A hard stop — an OOM kill, a container restart, a revert that itself failed — has no exit path,
and `MatchIngestionProcess` already documented the intended safety net ("candidates remain Processing until
the claim lease expires") without anything ever applying it to the rows. `MatchIngestion` now reaps expired
claims before it claims, so what a dead run left behind is claimable in the same pass.

**Recovery must not be gated on the membership the failure destroys.** The lease cutoff did exist, but only
inside `SelectClaimableAsync`, which reaches an account through one of two predicates: it holds an active
main, or it holds a `Queued` candidate. An account whose candidates were *all* stuck at `Processing` matched
neither, so it was invisible to the only mechanism that would have settled its rows — the leak sealed itself
and grew monotonically. Production had 1 185 rows across 498 accounts, 386 of them permanently unreachable,
accumulated from 2026-06-13 onward. Whenever a recovery path is filtered by state, check that the state it
filters on survives the failure it recovers from.

**The reaper releases what no live claim stands behind, not what carries an expired one.** The predicate is
negated on purpose: a candidate whose account row is gone has no claim at all, and an `EXISTS` on the expired
shape would leave it `Processing` forever. Both sides take the cutoff from the same
`MatchIngestion:ClaimLeaseMinutes` the claim uses, passed in by the caller, so the reaper cannot decide a
lease is spent while the claim still considers it held. Measured on production: 68 ms per pass at ~864 k
candidate rows, served by the existing `(PlatformId, Status, Score)` and partial claim indexes — no new index
(#1344).

## Postgres ships tuned settings in compose, and parallelism stays off (2026-09-02)

**The server no longer runs on compiled defaults.** Until now the only setting either compose file overrode
was `max_parallel_workers_per_gather=0`; everything else was stock `postgresql.conf`, sized for a 1 GB
machine, on a host where Postgres is the dominant tenant (prod: 4 vCPU, 16 GB RAM, NVMe, a 38 GB database,
8.8 GB RSS and ~13 GB of the host in page cache, measured 2026-09-02). `compose.prod.yaml` now passes a
pgtune-style "mixed" set of `-c` flags and `compose.preprod.yaml` the same *settings* at roughly half the
*values*, because preprod is a smaller host sharing its box with the whole preprod stack — same plans
exercised first, smaller footprint (#1366, part A).

**Why the risky ones.** `shared_buffers=4GB` is the conventional 25 % ceiling past which double buffering
against the OS page cache costs more than it returns. `effective_cache_size=11GB` is a planner hint only, and
it matches the cache actually observed — at the old 4 GB the planner under-priced index scans it should have
preferred. `work_mem=32MB` with `hash_mem_multiplier=2` is per sort/hash *node*, so the real ceiling is
several multiples of it per connection; it is affordable here only because pgbouncer caps the pool, and it is
what stops the 2.7 M-row `match_participants` folds from spilling to disk at 4 MB. `random_page_cost=1.1` and
`effective_io_concurrency=200` say the volume is flash, not a spinning disk. `jit=off` because JIT compilation
is pure overhead on queries this short. The autovacuum scale factors drop to 0.05/0.02 because the largest
tables bloat far faster than the stock 0.2 reacts.

**`max_parallel_workers_per_gather=0` stays, permanently.** It is not a leftover to clean up while tuning the
neighbouring settings: it is the fix for the /dev/shm exhaustion incident (#589), where parallel workers on
the aggregate-pattern queries exhausted the shared-memory segment, Postgres raised 53100, and the API and
ingestor crash-looped. Both compose files carry a comment saying so. `shm_size` went 256m → 1g as insurance
now that the server is allowed real memory, but that does not make parallelism safe to re-enable.

**`pg_stat_statements` is preloaded by compose and created by a migration, and the migration is tolerant.**
`shared_preload_libraries` is a start-up setting, so `CREATE EXTENSION` fails on any server that does not
carry it — a developer's local Postgres, the integration suite's throwaway container, a restored dump. The
migration wraps the statement in a `DO` block that catches the failure and raises a `NOTICE`, so a database
without the preload does not break the migration chain. The consequence, and it is the general rule for any
migration that depends on a setting shipped with it: the `migrate-*` job runs *before* the deploy restarts
Postgres, so the first run always takes the NOTICE branch and is stamped in `__EFMigrationsHistory` anyway —
nothing retries it. Each environment gets one manual `CREATE EXTENSION IF NOT EXISTS pg_stat_statements;`
after the restart, documented in `docs/production-migrations.md`.

**Both changes only take effect on a Postgres restart.** `shared_buffers` and `shared_preload_libraries` are
start-up settings. Preprod restarts on the next deploy from `develop`; prod moves only on a published
release.

## The patch is a column on `matches`, not a `LIKE` prefix over `GameVersion` (2026-09-02)

Every champion read narrows to one patch, and until #1368 every one of them did it the same way:
`EF.Functions.Like(m.GameVersion, '16.17.%')`. `PatchFilter`'s own comment said the quiet part out loud —
that predicate is **never index-assisted**. There is no index on `GameVersion`, and Postgres only turns a
`LIKE` prefix into a range scan for a *literal* pattern under a `text_pattern_ops` index, never for a
parameter. With `max_parallel_workers_per_gather = 0` (#589, and it stays off), each champion read was a
single-threaded scan of `matches` joined to `match_participants`. Measured cold on production on 2026-09-02:
roam 3.4–5.1 s, synergies ~2 s, the directory 2.1 s.

`matches."Patch"` is now a **stored generated column** holding the `major.minor` prefix, and the filter is a
plain equality on an indexed column. Two indexes, because there are two access shapes: `(Patch, QueueId)` for
the reads that filter one patch, and `(QueueId, Patch, PlatformId)` for the two writers that *enumerate*
patches — retention's live window and the pattern aggregation's live-key `DISTINCT`.

**Generated, not written by the ingestor.** The alternative was a plain column filled at insert time plus a
backfill, and it loses on the thing that matters: a second writer (a repair job, a manual `UPDATE`, a restored
dump) can put a row in `matches` whose `Patch` disagrees with its `GameVersion`, and the disagreement is
invisible — the row simply vanishes from a patch's numbers. `GENERATED ALWAYS AS (...) STORED` makes that
unrepresentable, and costs nothing at read time. Stored rather than virtual because Postgres cannot index a
virtual generated column, which is the entire point.

**The SQL expression is a transcription of `PatchVersion.TryParse(...).ToMajorMinor()`, deliberately.** Same
answer for the awkward inputs — empty segments dropped, segments trimmed, `16.04.5` → `16.4` because each
segment is re-rendered through `::int`, and **NULL** where the C# rule returns "not a patch". One divergence,
on purpose: segments are capped at nine digits, so a ten-digit major that `int.TryParse` would still accept
yields NULL here instead of an out-of-range cast that would fail the INSERT. The expression lives in
`MatchConfiguration.PatchComputedColumnSql`, and `MatchPatchColumnIntegrationTests` runs the two
implementations against each other on real Postgres — nothing else ties them together, and a column that
quietly disagrees with the C# rule just drops rows out of every champion read.

**Not a startup migration.** Adding a STORED column rewrites the table under `ACCESS EXCLUSIVE` and the two
index builds follow it; at ~274 k rows that is seconds, but it goes out of band through the
`migrate-preprod`/`migrate-prod` job like every other migration (`docs/production-migrations.md`, #598). The
indexes are ordinary `CREATE INDEX`, not `CONCURRENTLY`: the rewrite already holds the strongest lock there
is, so concurrency would buy nothing and cost the ability to run inside the script's transaction.

## Champion reads are cached until the data changes, not for 60 seconds (2026-09-02)

The champion reads were each caching themselves, with their own `TryGetValue`/`Store` pair and a 60 s TTL —
and five of them (`ChampionBuildsQueryService`, the live matchup fold, `GetTrioSynergiesAsync`,
`ChampionTrendQueryService`, `ChampionPatchDiffQueryService`) were not caching at all. `RequestCoalescer`
existed but only the truemains leaderboard used it. With 173 champions × 5 lanes × rank brackets, a 60 s TTL
means practically every visit to a non-top champion pays the cold price, and nothing stopped ten concurrent
visitors from each paying it at once.

**One entry point, because the two halves only work together.** `IChampionReadCache.GetOrComputeAsync` caches
*and* single-flights *and* sizes the entry. A cache without single-flight turns each expiry into a stampede of
identical multi-second scans; single-flight without a cache re-runs the pass for the next visitor; and an
entry without a `Size` is silently dropped by the size-limited shared cache (see `ApiCache`). Leaving those
three as three things a new service must remember is how you get a read that looks cached and is not.
`ChampionReadCacheRegistrationTests` walks `ChampionsController`'s constructor — the DI surface of the
champion reads — and fails if any service behind it takes a raw `IMemoryCache` or no cache at all.

**Keyed by aggregation version, not by a clock.** These answers are folds over data the ingestor rewrites once
per aggregation cycle and never in between, so a 60 s TTL was throwing away answers that were still exactly
right. The key carries a token derived from `MAX("AggregatedAtUtc")` over `champion_aggregate_scopes`; a new
cycle changes the token and retires every entry at once, with nothing to enumerate or evict. The token read is
itself cached for 5 s and single-flighted, so it costs at most one `max()` every five seconds no matter how
much traffic arrives — the one thing that must not happen is the version probe becoming the new hot query. A
30-minute absolute expiry stays as a backstop: not for freshness, but so a token that somehow stops moving
cannot pin a stale answer for ever. An empty database is just another version (`none`), so a first-ever fold
invalidates the empty answers by moving the token.

**The owner of a coalesced pass waits it out.** `RequestCoalescer` grew an `ownerAwaitsToCompletion` flag for
this. The champion reads run on the caller's request-scoped `DbContext`, so if the caller that *started* the
pass abandoned its wait, its scope would be disposed underneath the shared work and every joiner would fail on
a disposed context. The leaderboard does not need the flag — it creates its own context — and it does not get
it.

## Keeping these files current

A PR that ships a user-facing feature, removes one, or reverses a decision here **must update
`features.md` / `decisions.md` in the same PR**. These files are the context a fresh session loads instead of
re-reading the codebase; stale entries are worse than missing ones.

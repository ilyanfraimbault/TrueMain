# Champion page — builds, power spikes, SSR prose

Part of the [decision log](../decisions.md). Format: **Decision** — why — `source`.

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

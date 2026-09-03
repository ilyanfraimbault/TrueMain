# Matchups and the draft tool

Part of the [decision log](../decisions.md). Format: **Decision** — why — `source`.

**Matchups are a pre-aggregated read table — explicitly revising the earlier live-self-join design.**
#90 chose a self-join "for simplicity, not volume". Prod measurement showed the aggregate is bounded by
dimensions rather than games: ~22.2k rows, a few MB, versus a self-join over ~35 GB running single-threaded.
Reads became ~13-row indexed selects — #606.

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
and the read-side comment asserted the two cohorts matched. The gate now lives in `Data/Aggregation/ChampionCohort.cs`
(named `MatchupCohort` until #1365 generalised it to all four folds) so the folds that write those rows cannot
drift apart from each other or from the pattern reader. **Champion
side only**: the opponent stays whoever held that lane, since narrowing both sides would measure mains-versus-mains,
a different and far thinner question. Because both folds are additive and flag-gated, tightening the gate corrects
nothing already written — the migration wipes the table and re-folds the retained window, which loses the matchups
of patches whose raw matches are already gone (accepted: the panel became per-patch in the same change, so those
patches were no longer readable anyway) — #1087.

**Every champion-page fold takes its cohort from one place, and a remake is not a game.**
#1087 fixed the matchup folds and stopped there, so two panels on the same page kept counting a different
population from the header above them: `ChampionSynergyAggregationProcess` and
`ChampionPowerspikeAggregationProcess` still gated on `RiotAccountId != null` — any tracked account, main or
not — while the header, the tier list, the builds and the matchups gated on `IsMain`. The gate is now
`Data/Aggregation/ChampionCohort.cs` (the generalised `MatchupCohort`), composed by all four folds: **tracked
account + `main_champion_stats.IsMain` + canonical `TeamPosition` + not a remake**. A unit test greps the four
fold sources for `RiotAccountId`, `IsMain`, a private copy of the canonical positions and `GameDurationSeconds`,
because the failure mode is not a wrong answer, it is a plausible line added to one fold that nobody re-compares
against the header. Three things this pins.
**The partner side stays everyone.** Only the queried/`SELF` side of a synergy pairing is a main; the ally is
whoever shared the game, because the expected value the metric subtracts is built from a partner near the
population mean (#922). Narrowing it would bias every synergy on the site rather than tighten it.
**Remakes are a duration floor, in one place.** Riot's `gameEndedInEarlySurrender` is not stored on
`match_participants` (checked, not assumed), so `ChampionCohort.MinimumGameDurationSeconds` (300) is the rule —
4 762 stored matches, 1.7% of production. The header keeps its own stricter 15-minute floor, which is not a
second opinion about remakes but a timeline-completeness rule (no build, no skill path); a test pins the
ordering of the two.
**The re-fold recovers the live window only.** The migration deletes the synergy, powerspike **and matchup**
rows of the patches that still have matches and re-arms all four per-match flags — the matchup table is in there
for the remake clause alone, since #1087 already gave it the right population and an additive fold cannot correct
what it has already added — deliberately *not* a
`TRUNCATE` like #1087's, because these two aggregates hold frozen patches whose source matches retention has
already deleted (#466) and which no re-fold could rebuild. So there is a seam, on purpose: frozen patches keep
synergy and powerspike numbers counting any tracked account, live ones count mains. Two further costs, accepted:
a live match whose dense timeline grid was already pruned to {5,10,15,20,30} (#772) re-folds its curve points but
no event spike, so the spikes panel goes thin on the live patches and refills forward (the same coverage bargain
#957 took); and `powerspike_sigma_stats` is emptied with them, since it carries no patch dimension and a re-fold
would otherwise add a match's spread to a total that already holds it — σ becomes the spread over the retained
window instead of a double-counted lifetime average — #1365.

**The matchups panel follows the page's patch filter on the global route, and deliberately does not on the player one.**
It forwarded position and elo but never patch, and its aggregate outlives the matches it was folded from, so the
panel spanned **16.12→16.15 (53 739 games) under a header reading 4 603** — two contradicting numbers a few
centimetres apart. The player-scoped slice stays cross-patch on purpose, for the opposite reason the global one
needed scoping: one player meets the same lane opponent a handful of times *in total*, so a patch filter would put
nearly every opponent under the 3-game per-player floor and empty the panel — #1087.

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

**A match's game counters and its lane counters are folded in one pass, off one flag.** They were two
processes — the matchup fold, then the lane fold a moment later — and that is representable only while nothing
the row key depends on can change in between. `elo_bracket` can: it is part of the key and is stamped
asynchronously by `MatchParticipantEloBracketEnrichment` once an account's first rank snapshot lands, and since
#1362 the fetch lane ingests matches while the aggregate lane is mid-run. So a match could be counted as a game
under one band and as a lane under another, splitting one match across two rows. Preprod showed the shape
exactly: 10 rows with `LaneGames = Games + 1`, every one of them in the unstamped `''` band, against 45 775 rows
where `Games >= LaneGames` held. The reader impact was small — the `''` band is only reachable through the
default "all ranks" view — but the two counters on a row are meant to describe one cohort, and there they
described two. One fold, one `MatchupLeadAggregated`, one read of the participants makes the split
unrepresentable rather than rare; the flag the lane fold used is dropped, and the live patches were re-folded
(#466 keeps the frozen ones as they were measured). The gold threshold keeps its own config section, because it
is a product judgement shared with the API's live pass, not a pacing knob — #1445.

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

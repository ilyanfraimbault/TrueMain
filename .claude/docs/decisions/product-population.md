# Population and rank scope of the champion pages (#1346)

Part of the [decision log](../decisions.md). Format: **Decision** — why — `source`.

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

# Admin portal — health panels, charts and vocabulary

Part of the [decision log](../decisions.md). Format: **Decision** — why — `source`.

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

**#1403 raised the palette to six, and the outcome chart is what spends them.** Asked for the candidate levels
on the existing curve rather than in new cards, that chart now draws five statuses plus the cumulative
demotion. The three added colours were picked by search over OKLab ΔE under four vision models
(normal/protan/deutan/tritan), not by eye, and the result is worth recording because it is two facts, not one:
*adjacency did not degrade* — the worst adjacent pair is still 11.1, the same emerald↔amber the trio already
had, so slot order remains the guarantee and series must keep taking the palette in declaration order — but
*separation over all pairs did*, down to 5.8 (red↔orange), which is below what colour alone can carry in a
six-entry legend. Six is therefore the ceiling, and it only works because every series' current value is
printed as text under the chart; past three series that text is the identity mechanism, not a courtesy.
(`lime-400` was the obvious sixth and is disqualified at ΔE 1.4 against amber — the same colour, to a
deuteranope.) The cumulative `validated` curve was dropped in the same move: the Validated *level* answers
"how big is the roster" with the real figure instead of a window-bound running total.

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

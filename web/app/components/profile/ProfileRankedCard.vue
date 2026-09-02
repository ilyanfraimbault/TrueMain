<script setup lang="ts">
import type { ProfileRanked } from '~~/shared/types/profile'
import type { RankHistoryEntry } from '~~/shared/types/rank-history'
import {
  rankScore,
  tierHex,
} from '~/utils/tiers'

const props = withDefaults(defineProps<{
  ranked: ProfileRanked | null
  history?: readonly RankHistoryEntry[]
  historyLoading?: boolean
}>(), {
  history: () => [],
  historyLoading: false,
})

const CHART_HEIGHT = 150
const DAY_MS = 24 * 60 * 60 * 1000

// ─── Chart series ─────────────────────────────────────────────────────────
// A single continuous series: the rank score at each snapshot.
//
// The tier is deliberately *not* modelled as one series per tier. Doing that
// gives every tier its own area path anchored at y = 0, and because the
// y-domain floats far above zero (a Challenger sits around 5500), the
// out-of-run zeros drag each path down through the entire plot. Every
// promotion or demotion then paints a full-height wedge where two tier areas
// overlap — the "triangles" the card used to show around short tier dips.
//
// The tier colour lives on an x-axis gradient instead, with one hard-edged
// stop pair per tier run (see `tierStops`), painted onto both the line and
// the fill so the whole chart colour-shifts at each transition without ever
// splitting into per-tier paths.
interface ChartPoint extends Record<string, unknown> {
  entry: RankHistoryEntry
  score: number
}

const chartPoints = computed<ChartPoint[]>(() =>
  props.history.map(entry => ({
    entry,
    score: rankScore(entry.tier, entry.division, entry.leaguePoints),
  })),
)

const currentTier = computed(() => props.ranked?.tier ?? null)

const categories = computed(() => ({
  score: { name: 'LP', color: tierHex(currentTier.value) },
}))

// One stop pair per contiguous tier run, positioned on the *midpoint*
// between it and each neighbouring run rather than on its own end points.
// Two consecutive runs then share their boundary stop at the exact same
// offset — an SVG hard stop, an instant colour change with no interpolated
// band — instead of each ending on its own last/first point and leaving a
// gap the gradient blends across. This also matters for a run that is a
// single snapshot: without the half-point extension toward each neighbour,
// its own start and end stops coincide and it never gets a visible band at
// all, just the blend from its neighbours fading through where it sat.
const tierStops = computed(() => {
  const points = chartPoints.value
  const last = points.length - 1
  if (last < 1) return []

  const stops: Array<{ offset: string, color: string }> = []
  let runStart = 0
  for (let i = 1; i <= last + 1; i++) {
    const runTier = points[runStart]!.entry.tier
    const sameRun = i <= last && points[i]!.entry.tier.toUpperCase() === runTier.toUpperCase()
    if (sameRun) continue
    const runEnd = i - 1
    const color = tierHex(runTier)
    const left = runStart === 0 ? 0 : (runStart - 0.5) / last * 100
    const right = runEnd === last ? 100 : (runEnd + 0.5) / last * 100
    stops.push({ offset: `${left.toFixed(3)}%`, color })
    stops.push({ offset: `${right.toFixed(3)}%`, color })
    runStart = i
  }
  return stops
})

// Shared by the line's stroke and the area's fill so both repaint from the
// same tier boundaries — see `tierStops`.
const tierGradientId = `rank-tier-gradient-${useId()}`

// The area's own vertical opacity fade, applied as a <mask> instead of via
// `gradient-stops` (vue-chrts' own per-category gradient) because that
// gradient bakes in a *flat* colour — it can't also carry the horizontal
// tier-boundary hues `tierGradientId` needs. Same opacity numbers as the
// fade this replaces, so only the hue changes.
const areaFadeGradientId = `rank-area-fade-gradient-${useId()}`
const areaFadeMaskId = `rank-area-fade-mask-${useId()}`

// An objectBoundingBox gradient needs a non-degenerate box: a flat history
// gives the line path zero height and the browser drops the element
// altogether, so single-tier and dead-flat histories keep a plain colour.
const lineStroke = computed(() => {
  const scores = chartPoints.value.map(p => p.score)
  const distinctTiers = new Set(chartPoints.value.map(p => p.entry.tier.toUpperCase())).size
  if (distinctTiers < 2 || Math.min(...scores) === Math.max(...scores)) {
    return tierHex(currentTier.value)
  }
  return `url(#${tierGradientId})`
})

// The area has two degenerate cases of its own:
//  - A single-point history leaves `tierStops` empty (see its `last < 1`
//    guard), so the gradient it would feed has no `<stop>` children and
//    resolves to `none` — an invisible fill.
//  - A flat history pinned at score 0 (Iron IV 0 LP, reachable — see
//    `rankScore`'s docstring — by an inactive account whose whole tracked
//    window sits at the ladder floor) hits the same zero-height
//    bounding-box case as the line, but for a different reason: the area's
//    baseline defaults to the data value 0 (`Area`'s `baseline: () => 0` in
//    `@unovis/ts`), so with every point *also* at 0 its top and bottom
//    edges coincide exactly, regardless of the y-domain padding.
const areaFill = computed(() => {
  if (tierStops.value.length === 0) return tierHex(currentTier.value)
  const scores = chartPoints.value.map(p => p.score)
  if (Math.min(...scores) === 0 && Math.max(...scores) === 0) return tierHex(currentTier.value)
  return `url(#${tierGradientId})`
})

// Pad the Y range by 25% (min 50 LP-equivalents) so the line never hugs
// the top/bottom edge. Falls back to [0, 400] (Iron band) when there's
// nothing to plot so the empty chart has a sensible scale.
const yDomain = computed<[number, number]>(() => {
  if (chartPoints.value.length === 0) return [0, 400]
  const scores = chartPoints.value.map(p => p.score)
  const minScore = Math.min(...scores)
  const maxScore = Math.max(...scores)
  const padded = Math.max(50, (maxScore - minScore) * 0.25)
  return [Math.max(0, minScore - padded), maxScore + padded]
})

// Tier crests rendered alongside the chart. One crest per *tier* present
// in the visible history — division changes within the same tier
// (Emerald IV → Emerald II, etc.) don't add a crest. Each crest is pinned
// at the median score of that tier's run so apex bands (Master, GM,
// Challenger) — which share a single tier floor — still stack vertically
// in climb order instead of collapsing to the same Y.
const visibleTiers = computed(() => {
  if (chartPoints.value.length === 0) return []
  const buckets = new Map<string, number[]>()
  for (const point of chartPoints.value) {
    const tier = point.entry.tier.toUpperCase()
    const arr = buckets.get(tier)
    if (arr) arr.push(point.score)
    else buckets.set(tier, [point.score])
  }
  return Array.from(buckets, ([tier, scores]) => {
    const sorted = scores.slice().sort((a, b) => a - b)
    const median = sorted[Math.floor(sorted.length / 2)]!
    return { tier, score: median }
  }).sort((a, b) => a.score - b.score)
})

function tierTopPx(score: number): number {
  const [yMin, yMax] = yDomain.value
  if (yMax === yMin) return CHART_HEIGHT / 2
  const ratio = (score - yMin) / (yMax - yMin)
  return CHART_HEIGHT * (1 - ratio)
}

function dateLabel(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    month: 'short',
    day: 'numeric',
  })
}

const xFormatter = (tick: number): string => {
  const point = chartPoints.value[tick]
  return point ? dateLabel(point.entry.capturedAtUtc) : ''
}

// ─── LP deltas ────────────────────────────────────────────────────────────
// "Last 30d / 7d" badges compare the latest rank score to the last snapshot
// at-or-before the cutoff, falling back to the earliest tracked snapshot
// when the player has no older data. Returns null when there's no
// meaningful comparison (≤1 snapshot, or the cutoff snapshot is the
// current one).
function deltaSince(days: number): number | null {
  if (chartPoints.value.length < 2) return null
  const cutoff = Date.now() - days * DAY_MS
  let base: ChartPoint | undefined
  for (const point of chartPoints.value) {
    const t = new Date(point.entry.capturedAtUtc).getTime()
    if (t <= cutoff) base = point
    else break
  }
  base ??= chartPoints.value[0]
  const current = chartPoints.value[chartPoints.value.length - 1]
  if (!base || !current || base === current) return null
  return current.score - base.score
}

const delta30d = computed(() => deltaSince(30))
const delta7d = computed(() => deltaSince(7))
const hasDeltas = computed(() => delta30d.value !== null || delta7d.value !== null)

const showEmptyChart = computed(
  () => !props.historyLoading && chartPoints.value.length === 0 && props.ranked !== null,
)
</script>

<template>
  <section class="flex flex-col gap-3 surface rounded-lg px-4 py-3">
    <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
      Ranked Solo/Duo
    </h2>

    <template v-if="ranked">
      <RankSummary
        :tier="ranked.tier"
        :division="ranked.division"
        :league-points="ranked.leaguePoints"
        :wins="ranked.wins"
        :losses="ranked.losses"
        :win-rate="ranked.winRate"
        :size="48"
      />

      <div v-if="hasDeltas" class="flex flex-wrap gap-2">
        <span
          v-if="delta30d !== null"
          class="inline-flex items-center gap-1.5 rounded-md bg-elevated px-2 py-1 text-xs"
        >
          <span class="text-muted">Last 30d</span>
          <UIcon
            :name="delta30d >= 0 ? 'i-lucide-trending-up' : 'i-lucide-trending-down'"
            class="size-3.5"
            :class="delta30d >= 0 ? 'text-data-good' : 'text-data-bad'"
          />
          <span class="font-semibold tabular-nums text-default">
            {{ Math.abs(delta30d) }} LP
          </span>
        </span>
        <span
          v-if="delta7d !== null"
          class="inline-flex items-center gap-1.5 rounded-md bg-elevated px-2 py-1 text-xs"
        >
          <span class="text-muted">Last 7d</span>
          <UIcon
            :name="delta7d >= 0 ? 'i-lucide-trending-up' : 'i-lucide-trending-down'"
            class="size-3.5"
            :class="delta7d >= 0 ? 'text-data-good' : 'text-data-bad'"
          />
          <span class="font-semibold tabular-nums text-default">
            {{ Math.abs(delta7d) }} LP
          </span>
        </span>
      </div>
    </template>
    <p v-else class="text-base text-muted">
      Unranked
    </p>

    <USkeleton v-if="historyLoading" class="h-[150px] w-full rounded-md" />

    <p v-else-if="showEmptyChart" class="text-sm text-muted">
      No ranked snapshots in the last 90 days.
    </p>

    <div v-else-if="chartPoints.length > 0" class="rank-chart flex gap-2">
      <!-- Paint servers for the line's stroke and the area's fill. They live
           in their own zero-sized SVG because the chart's own <defs> are
           owned by the upstream component; `url(#…)` references resolve
           document-wide, so they still apply inside the chart's SVG. -->
      <svg class="absolute size-0 overflow-hidden" aria-hidden="true" focusable="false">
        <defs>
          <linearGradient :id="tierGradientId" x1="0" y1="0" x2="1" y2="0">
            <stop
              v-for="(stop, i) in tierStops"
              :key="i"
              :offset="stop.offset"
              :stop-color="stop.color"
            />
          </linearGradient>
          <linearGradient :id="areaFadeGradientId" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stop-color="white" stop-opacity="0.45" />
            <stop offset="100%" stop-color="white" stop-opacity="0.05" />
          </linearGradient>
          <mask :id="areaFadeMaskId" maskContentUnits="objectBoundingBox">
            <rect x="0" y="0" width="1" height="1" :fill="`url(#${areaFadeGradientId})`" />
          </mask>
        </defs>
      </svg>

      <!-- Y-axis: tier crests stacked at their score band. The wrapping
           column shares the chart's exact height so absolute offsets in
           `tierTopPx` line up with the data range. -->
      <div
        class="relative w-7 shrink-0"
        :style="{ height: `${CHART_HEIGHT}px` }"
        aria-hidden="true"
      >
        <RankIcon
          v-for="band in visibleTiers"
          :key="band.tier"
          :tier="band.tier"
          :size="20"
          class="absolute left-0 -translate-y-1/2"
          :style="{ top: `${tierTopPx(band.score)}px` }"
        />
      </div>

      <div class="min-w-0 flex-1">
        <ChartsAreaChart
          :data="chartPoints"
          :categories="categories"
          :height="CHART_HEIGHT"
          :x-formatter="xFormatter"
          :y-domain="yDomain"
          hide-y-axis
          hide-legend
        >
          <template #tooltip="{ values }">
            <div
              v-if="values"
              class="rounded-md border border-default bg-elevated px-2 py-1.5 text-xs shadow-md"
            >
              <div class="flex items-center gap-1.5">
                <RankIcon :tier="values.entry.tier" :size="20" />
                <span class="font-semibold tabular-nums text-default">
                  {{ values.entry.leaguePoints }} LP
                </span>
              </div>
              <p class="mt-0.5 text-muted">
                {{ dateLabel(values.entry.capturedAtUtc) }}
              </p>
            </div>
          </template>
        </ChartsAreaChart>
      </div>
    </div>
  </section>
</template>

<style scoped>
/*
 * Repaint the rank line with the tier gradient. Unovis writes the stroke as a
 * presentation attribute (`.attr('stroke', …)` in @unovis/ts
 * `components/line/index.js`), which a plain CSS declaration outranks without
 * needing `!important`. Emotion suffixes the path's generated class with
 * "-linePath" (see `label: linePath` in that package's `components/line/
 * style.js`) — same targeting trick as the tooltip override in main.css.
 */
.rank-chart :deep([class*="-linePath"]) {
  stroke: v-bind(lineStroke);
}

/*
 * Repaint the area's fill with the same tier gradient, faded vertically by
 * the mask instead of vue-chrts' own per-category gradient (which only ever
 * carries one flat colour — see `areaFadeGradientId`). Unlike the line's
 * stroke, Unovis sets this `fill` via `.style(...)` (@unovis/ts
 * `components/area/index.js`), i.e. an *inline* style, which only an
 * `!important` declaration outranks. The selector is tag-qualified —
 * `-area-component` (the wrapping <g>) also matches a bare class substring
 * of "-area", and letting the rule land there too would apply the mask
 * twice through inheritance and double-darken the fade.
 */
.rank-chart :deep(path[class*="-area"]) {
  fill: v-bind(areaFill) !important;
  mask: v-bind('`url(#${areaFadeMaskId})`');
}
</style>

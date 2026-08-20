<script setup lang="ts">
import type { ProfileRanked } from '~~/shared/types/profile'
import type { RankHistoryEntry } from '~~/shared/types/rank-history'
import { formatPercentage } from '~~/shared/utils/ddragon'
import {
  formatTier,
  rankScore,
  tierColor,
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

// ─── Headline ─────────────────────────────────────────────────────────────
const tierClass = computed(() => tierColor(props.ranked?.tier ?? null))
const tierLabel = computed(() => {
  if (!props.ranked) return null
  return formatTier(props.ranked.tier, props.ranked.division)
})

const recordLabel = computed(() => {
  if (!props.ranked) return null
  const w = props.ranked.wins
  const l = props.ranked.losses
  if (w === null && l === null) return null
  const record = `${w ?? '?'}W – ${l ?? '?'}L`
  const wr = props.ranked.winRate === null ? null : formatPercentage(props.ranked.winRate, 0)
  return wr ? `${record} (${wr})` : record
})

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
// The tier colour lives on the *line* instead, as an x-axis gradient with one
// hard-edged stop pair per tier run (see `tierStops`), which colour-shifts at
// each transition without ever splitting the fill.
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

// One stop pair per contiguous tier run, positioned on the point index the
// run starts and ends at. Equal offsets inside a run keep it a flat colour;
// the gap between two runs spans exactly the segment that crosses the
// promotion, so the two tier colours blend across it.
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
    const color = tierHex(runTier)
    stops.push({ offset: `${(runStart / last * 100).toFixed(3)}%`, color })
    stops.push({ offset: `${((i - 1) / last * 100).toFixed(3)}%`, color })
    runStart = i
  }
  return stops
})

const gradientId = `rank-line-gradient-${useId()}`

// An objectBoundingBox gradient needs a non-degenerate box: a flat history
// gives the line path zero height and the browser drops the element
// altogether, so single-tier and dead-flat histories keep a plain colour.
const lineStroke = computed(() => {
  const scores = chartPoints.value.map(p => p.score)
  const distinctTiers = new Set(chartPoints.value.map(p => p.entry.tier.toUpperCase())).size
  if (distinctTiers < 2 || Math.min(...scores) === Math.max(...scores)) {
    return tierHex(currentTier.value)
  }
  return `url(#${gradientId})`
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
      <div class="flex items-center gap-3">
        <RankIcon :tier="ranked.tier" :size="48" />
        <div class="flex min-w-0 flex-col leading-tight">
          <span class="text-base font-bold tabular-nums" :class="tierClass">
            {{ tierLabel }}
            <span class="text-default">{{ ranked.leaguePoints }} LP</span>
          </span>
          <span v-if="recordLabel" class="mt-1 text-sm text-muted tabular-nums">
            {{ recordLabel }}
          </span>
        </div>
      </div>

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
      <!-- Paint server for the rank line. It lives in its own zero-sized SVG
           because the chart's own <defs> are owned by the upstream component;
           `url(#…)` references resolve document-wide, so the gradient still
           applies to the line inside the chart's SVG. -->
      <svg class="absolute size-0 overflow-hidden" aria-hidden="true" focusable="false">
        <defs>
          <linearGradient :id="gradientId" x1="0" y1="0" x2="1" y2="0">
            <stop
              v-for="(stop, i) in tierStops"
              :key="i"
              :offset="stop.offset"
              :stop-color="stop.color"
            />
          </linearGradient>
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
          :gradient-stops="[
            { offset: '0%', stopOpacity: 0.45 },
            { offset: '100%', stopOpacity: 0.05 },
          ]"
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
</style>

<script setup lang="ts">
import type { ProfileRanked } from '~~/shared/types/profile'
import type { RankHistoryEntry } from '~~/shared/types/rank-history'
import { formatPercentage } from '~~/shared/utils/ddragon'
import {
  formatTier,
  rankScore,
  TIER_NAMES,
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
// We render one series per tier present in the history so the line / area
// fill colour-shifts at each promotion or demotion. Each chart point keeps
// its rank-score under the *current* tier's key and leaves every other
// tier `undefined`, which makes Unovis break that tier's line outside its
// run. The boundary snapshot is duplicated under both adjacent tiers so
// the segments visually meet rather than leaving a gap at the transition.
interface ChartPoint extends Record<string, unknown> {
  entry: RankHistoryEntry
}

// Plain score series, used for both delta math and the y-domain. Decoupled
// from `chartPoints` because the multi-tier shape there would force every
// consumer to scan tier keys to find a numeric value.
const scoreSeries = computed(() =>
  props.history.map(entry => ({
    entry,
    score: rankScore(entry.tier, entry.division, entry.leaguePoints),
  })),
)

const chartPoints = computed<ChartPoint[]>(() => {
  const out: ChartPoint[] = []
  let prevTier: string | null = null
  for (const item of scoreSeries.value) {
    const tier = item.entry.tier.toUpperCase()
    const point: ChartPoint = { entry: item.entry }
    point[tier] = item.score
    if (prevTier !== null && prevTier !== tier) {
      // Carry the boundary score back into the previous tier's series so its
      // line ends where the next one starts.
      point[prevTier] = item.score
    }
    out.push(point)
    prevTier = tier
  }
  return out
})

// Distinct tiers in the visible history, ordered low-to-high so categories
// iterate Iron → Challenger and the SVG stacking order reflects rank.
const presentTiers = computed(() => {
  const seen = new Set<string>()
  for (const entry of props.history) seen.add(entry.tier.toUpperCase())
  return Array.from(seen).sort((a, b) => TIER_NAMES.indexOf(a) - TIER_NAMES.indexOf(b))
})

const categories = computed(() => {
  const out: Record<string, { name: string, color: string }> = {}
  for (const tier of presentTiers.value) {
    out[tier] = { name: tier, color: tierHex(tier) }
  }
  return out
})

const currentTier = computed(() => props.ranked?.tier ?? null)

// Pad the Y range by 25% (min 50 LP-equivalents) so the line never hugs
// the top/bottom edge. Falls back to [0, 400] (Iron band) when there's
// nothing to plot so the empty chart has a sensible scale.
const yDomain = computed<[number, number]>(() => {
  if (scoreSeries.value.length === 0) return [0, 400]
  const scores = scoreSeries.value.map(p => p.score)
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
  if (scoreSeries.value.length === 0) return []
  const buckets = new Map<string, number[]>()
  for (const point of scoreSeries.value) {
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
  if (scoreSeries.value.length < 2) return null
  const cutoff = Date.now() - days * DAY_MS
  let base: { entry: RankHistoryEntry, score: number } | undefined
  for (const point of scoreSeries.value) {
    const t = new Date(point.entry.capturedAtUtc).getTime()
    if (t <= cutoff) base = point
    else break
  }
  base ??= scoreSeries.value[0]
  const current = scoreSeries.value[scoreSeries.value.length - 1]
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
  <section class="flex flex-col gap-3 glass rounded-lg px-4 py-3">
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
            :class="delta30d >= 0 ? 'text-emerald-400' : 'text-red-400'"
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
            :class="delta7d >= 0 ? 'text-emerald-400' : 'text-red-400'"
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

    <div v-else-if="chartPoints.length > 0" class="flex gap-2">
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

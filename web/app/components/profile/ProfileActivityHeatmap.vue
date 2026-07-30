<script setup lang="ts">
import type { ActivityBucket, ActivityMode, TruemainActivityResponse } from '~~/shared/types/activity'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import {
  activityBucketLabel,
  activityBucketResult,
  activityCellFill,
  activityCoverageNote,
  activityMaxGames,
} from '~/utils/activity-heatmap'

/**
 * dpm.lol-style activity grid under the LP curve (#927). Four granularities over
 * one payload: the mode switch is purely local, because the response already
 * carries all four series (three of them folded from the same games) — so
 * flipping it can never show two different answers for the same afternoon.
 *
 * The card's job beyond drawing squares is to keep two facts legible that the
 * data itself cannot enforce:
 *
 * - **An empty period is not a lost one.** A cell with no games gets an outline
 *   and no fill; a cell whose games were all losses gets a rose fill. They must
 *   never look alike, which is why the fill helper returns `null` rather than a
 *   0%-coloured value.
 * - **The modes do not cover the same thing.** Game / day / week read live match
 *   rows, which retention deletes after ~2 patches, and cover every champion.
 *   Patch reads the frozen per-champion aggregate, which is kept forever but
 *   only for the player's signature champion. The coverage line under the grid
 *   states which of the two the reader is looking at, off the payload's own
 *   `source` / `scope` fields.
 */
const props = withDefaults(defineProps<{
  data: TruemainActivityResponse | null
  /** Champion statics, to name the champion the patch series is scoped to. */
  champions?: ChampionStaticListItem[]
  loading?: boolean
}>(), {
  champions: () => [],
  loading: false,
})

const MODES: { key: ActivityMode, label: string }[] = [
  { key: 'game', label: 'Game' },
  { key: 'day', label: 'Day' },
  { key: 'week', label: 'Week' },
  { key: 'patch', label: 'Patch' },
]

// Day is the default: it is the granularity the grid is shaped for (~a month of
// squares) and the one where an idle cell carries the most meaning.
const mode = ref<ActivityMode>('day')

const series = computed(() => props.data?.[mode.value] ?? null)

const maxGames = computed(() => (series.value ? activityMaxGames(series.value) : 0))

const championName = computed(() => {
  const championId = props.data?.patch.championId
  if (championId === null || championId === undefined) return null
  return props.champions.find(champion => champion.championId === championId)?.name ?? null
})

const coverageNote = computed(() =>
  series.value ? activityCoverageNote(series.value, championName.value) : null)

interface Cell {
  bucket: ActivityBucket
  fill: string | null
  label: string
  result: string
}

const cells = computed<Cell[]>(() => {
  const current = series.value
  if (!current) return []
  return current.buckets.map(bucket => ({
    bucket,
    fill: activityCellFill(bucket, maxGames.value),
    label: activityBucketLabel(bucket, current.mode),
    result: activityBucketResult(bucket),
  }))
})

// Hovered-cell readout. A 60-square grid with a tooltip component per cell would
// mount 60 poppers; one shared line above the grid reads better anyway, and the
// native `title` on each cell keeps the information reachable without a pointer.
const hovered = ref<Cell | null>(null)

const summary = computed(() => {
  const current = series.value
  if (!current) return null
  if (current.games === 0) return null
  const losses = current.games - current.wins
  const rate = current.winRate === null ? null : `${Math.round(current.winRate * 100)}%`
  return rate
    ? `${current.games} games · ${current.wins}W – ${losses}L · ${rate}`
    : `${current.games} games`
})

const isEmpty = computed(() => cells.value.length === 0)
</script>

<template>
  <section class="flex flex-col gap-2">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
        Activity
      </h2>
      <div class="flex gap-0.5">
        <UButton
          v-for="option in MODES"
          :key="option.key"
          size="xs"
          :variant="mode === option.key ? 'soft' : 'ghost'"
          :color="mode === option.key ? 'primary' : 'neutral'"
          @click="() => { mode = option.key; hovered = null }"
        >
          {{ option.label }}
        </UButton>
      </div>
    </div>

    <div class="glass flex flex-col gap-2 rounded-lg p-3">
      <USkeleton v-if="loading" class="h-24 w-full rounded-md" />

      <template v-else-if="!data">
        <p class="py-2 text-sm text-muted">
          Activity is unavailable right now.
        </p>
      </template>

      <template v-else>
        <!-- Readout line: the hovered cell, or the series total when nothing is
             hovered. Fixed to one line so hovering never reflows the grid. -->
        <p class="min-h-4 truncate text-xs text-muted tabular-nums">
          <template v-if="hovered">
            <span class="font-medium text-default">{{ hovered.label }}</span>
            · {{ hovered.result }}
          </template>
          <template v-else-if="summary">{{ summary }}</template>
        </p>

        <p v-if="isEmpty" class="py-2 text-sm text-muted">
          Nothing to plot for this view.
        </p>

        <!-- Ten columns whatever the mode, so a square keeps the same size when
             the reader flips between 12 weeks and 60 games. -->
        <ul v-else class="grid grid-cols-10 gap-1">
          <li
            v-for="cell in cells"
            :key="cell.bucket.key"
            class="aspect-square rounded-[3px] transition-colors"
            :class="cell.fill
              ? 'ring-1 ring-inset ring-white/5'
              : 'ring-1 ring-inset ring-default/60'"
            :style="cell.fill ? { backgroundColor: cell.fill } : undefined"
            :title="`${cell.label} — ${cell.result}`"
            :aria-label="`${cell.label} — ${cell.result}`"
            @mouseenter="hovered = cell"
            @mouseleave="hovered = null"
          />
        </ul>

        <p v-if="coverageNote" class="text-[11px] leading-snug text-dimmed">
          {{ coverageNote }}
        </p>
      </template>
    </div>
  </section>
</template>

<script setup lang="ts">
import type { ActivityMode, TruemainActivityResponse } from '#shared/types/activity'
import { formatPercentage } from '#shared/utils/ddragon'
import {
  type ActivityCell,
  ACTIVITY_EMPTY_FILL,
  activityBucketLabel,
  activityBucketResult,
  activityCellFill,
  activityCellsAreGames,
  activityMaxGames,
} from '#common/utils/activity-heatmap'

/**
 * dpm.lol-style activity grid under the LP curve (#927, reshaped in #1473).
 *
 * **The unit is the day; the switch picks the window.** Every square is one UTC
 * day — of the last thirty, of the current patch, or of the last seven — and the
 * only exception is the narrowest window of all, where a single day has no days
 * left to draw and the squares become that day's games. The earlier version changed unit with
 * every tab (a game, a day, a week, a whole patch), so the same switch that was
 * meant to zoom also silently changed what a square meant.
 *
 * Three rules the data cannot enforce on its own.
 *
 * **The squares say how much was played, not how it went.** One rose-gold ramp,
 * four steps, keyed on games. The record and the rate are on the card's own
 * summary and on every cell's tooltip; spending the grid's only channel on them
 * a third time is what made half of a normal month render as switched off
 * (#1452 — see `decisions/design-system.md`).
 *
 * **Every day of the window is drawn, played or not.** A day with no games gets
 * its own tile, painted clearly above the card surface. Skipping it — or fading
 * it into the background — leaves a hole where a fact should be, and the run of
 * idle tiles between two sessions is exactly what makes a busy stretch legible.
 * That is why the patch window is measured over everyone's matches server-side:
 * the days before this player's first game of the patch are days they sat out.
 *
 * **The grid is squares and nothing else.** Small, fixed, identical tiles packed
 * from the left — never stretched to fill the card. Stretching them was tried
 * (#1473) and rejected by the product owner: an eleven-day patch became eleven
 * fat lozenges, which reads as a row of buttons rather than as a contribution
 * grid, and the shape of a month is carried by the *density* of small tiles.
 * With it went everything printed around them — a date under every tile, a
 * ramp legend, and a "from – to" line repeating dates the tooltip already
 * gives. What is left on the card is the summary and the squares; per-cell
 * facts live one hover away.
 */
const props = withDefaults(defineProps<{
  data: TruemainActivityResponse | null
  loading?: boolean
  /**
   * The windows on offer, a subset of `MODES`. The desktop dashboard folds the
   * grid from the games its client returned, which carry no patch, so it offers
   * day, week and month only and opens on month.
   */
  modes?: ActivityMode[]
}>(), {
  loading: false,
  modes: () => ['day', 'week', 'month', 'patch'],
})

/**
 * The switch, narrowest window first — a day, then a week, then a month. Patch
 * closes the row rather than sorting by width: it is the only window whose span
 * is not a fixed number of days, so it sits at the end of the calendar ladder
 * instead of interrupting it between week and month.
 */
const MODES: { key: ActivityMode, label: string }[] = [
  { key: 'day', label: 'Day' },
  { key: 'week', label: 'Week' },
  { key: 'month', label: 'Month' },
  { key: 'patch', label: 'Patch' },
]

// Patch is the default: it is the widest window the retained data can back, and
// the one an idle cell carries the most meaning in.
const offered = computed(() => MODES.filter(option => props.modes.includes(option.key)))
const mode = ref<ActivityMode>(props.modes.includes('patch') ? 'patch' : 'month')

const series = computed(() => props.data?.[mode.value] ?? null)

const maxGames = computed(() => (series.value ? activityMaxGames(series.value) : 0))

// Whether this window's cells are games rather than days. Read off the mode, not
// guessed from the busiest cell: a patch on which the player never queued twice
// in a day is still made of days.
const perGame = computed(() => activityCellsAreGames(mode.value))

const cells = computed<ActivityCell[]>(() => {
  const current = series.value
  if (!current) return []
  return current.buckets.map((bucket) => {
    const fill = activityCellFill(bucket, maxGames.value, perGame.value)
    return {
      bucket,
      fill: fill ?? ACTIVITY_EMPTY_FILL,
      label: activityBucketLabel(bucket, current.mode),
      result: activityBucketResult(bucket),
    }
  })
})

// The grid owns its hover panel; a press on the window switch closes it.
const grid = useTemplateRef<{ hideTooltip: () => void }>('grid')

function selectMode(value: ActivityMode) {
  mode.value = value
  grid.value?.hideTooltip()
}

const summary = computed(() => {
  const current = series.value
  if (!current || current.games === 0) return null
  // Same wins-over-games shape as the tooltips, so the total and a cell are read
  // the same way rather than in two different notations one card apart.
  return {
    rate: current.winRate === null ? null : formatPercentage(current.winRate, 0),
    record: `${current.wins}W – ${current.games - current.wins}L`,
    // Singular matters here now that the day window routinely holds one game.
    games: `${current.games} ${current.games === 1 ? 'game' : 'games'}`,
  }
})

const isEmpty = computed(() => cells.value.length === 0)
</script>

<template>
  <section class="flex flex-col gap-2">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
        Activity
      </h2>
      <ProfileActivityModeSwitch
        :model-value="mode"
        :options="offered"
        @update:model-value="selectMode"
      />
    </div>

    <div class="surface flex flex-col gap-3 rounded-lg p-4">
      <USkeleton v-if="loading" class="h-24 w-full rounded-md" />

      <p v-else-if="!data" class="py-2 text-sm text-muted">
        Activity is unavailable right now.
      </p>

      <!-- The only window that can come back with nothing is the day one: a rest
           day has no games to draw, and there is no such thing as an idle game.
           The calendar windows always carry their days. -->
      <p v-else-if="isEmpty" class="py-2 text-sm text-muted">
        {{ mode === 'day' ? 'No games today.' : 'Nothing to plot for this view.' }}
      </p>

      <template v-else>
        <!-- The window's total, and nothing else. The dates that used to sit
             opposite it (which patch, first day – last day) were the window's own
             definition read back to the reader, and every one of them is on the
             tooltip of the cell they belong to. -->
        <p v-if="summary" class="flex items-baseline gap-2 tabular-nums">
          <span v-if="summary.rate" class="text-lg font-semibold leading-none text-highlighted">
            {{ summary.rate }}
          </span>
          <span class="text-xs text-muted">{{ summary.record }} · {{ summary.games }}</span>
        </p>
        <p v-else class="text-xs text-muted">
          No games in this view.
        </p>

        <ProfileActivityGrid
          ref="grid"
          :cells="cells"
        />
      </template>
    </div>
  </section>
</template>

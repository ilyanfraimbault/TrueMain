<script setup lang="ts">
import type { ActivityBucket, ActivityMode, TruemainActivityResponse } from '~~/shared/types/activity'
import {
  ACTIVITY_EMPTY_FILL,
  activityBucketLabel,
  activityBucketResult,
  activityCellFill,
  activityCellsAreGames,
  activityMaxGames,
} from '~/utils/activity-heatmap'

/**
 * dpm.lol-style activity grid under the LP curve (#927, reshaped in #1473).
 *
 * **The unit is the day; the switch picks the window.** Every square is one UTC
 * day — of the current patch, or of the last seven — and the only exception is
 * the narrowest window of all, where a single day has no days left to draw and
 * the squares become that day's games. The earlier version changed unit with
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
}>(), {
  loading: false,
})

const MODES: { key: ActivityMode, label: string }[] = [
  { key: 'patch', label: 'Patch' },
  { key: 'week', label: 'Week' },
  { key: 'day', label: 'Day' },
]

/**
 * Tile geometry, in pixels — one size for every window, because a square that
 * changed size with the switch would be one more thing the control silently
 * re-labels. `auto-fill` at a *fixed* track width (not `minmax(_, 1fr)`) is what
 * keeps them square and packed left instead of stretching to share the row.
 */
const TILE = 14
const GAP = 4

// Patch is the default: it is the widest window the retained data can back, and
// the one an idle cell carries the most meaning in.
const mode = ref<ActivityMode>('patch')

const series = computed(() => props.data?.[mode.value] ?? null)

const maxGames = computed(() => (series.value ? activityMaxGames(series.value) : 0))

interface Cell {
  bucket: ActivityBucket
  fill: string
  label: string
  result: string
}

// Whether this window's cells are games rather than days. Read off the mode, not
// guessed from the busiest cell: a patch on which the player never queued twice
// in a day is still made of days.
const perGame = computed(() => activityCellsAreGames(mode.value))

const cells = computed<Cell[]>(() => {
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

const gridStyle = {
  gap: `${GAP}px`,
  gridTemplateColumns: `repeat(auto-fill, ${TILE}px)`,
}

/*
 * Hover tooltip. A `UTooltip` per cell would mount sixty poppers for one grid,
 * so this is a single panel moved to whichever square the pointer is on —
 * anchored inside the grid, which is `relative` for exactly that reason.
 *
 * The position is taken from the cell's own offsets rather than from
 * `getBoundingClientRect`, so scrolling the page cannot desync the panel from
 * its square. It is clamped to the wrapper after the panel has rendered, because
 * clamping needs its measured width: a square at either edge would otherwise
 * push the panel past the card.
 */
const hovered = ref<Cell | null>(null)
const tooltipAnchor = ref<{ x: number, y: number, height: number, above: boolean } | null>(null)
const tooltipEl = useTemplateRef<HTMLElement>('tooltipEl')
// The positioned ancestor the tooltip's `left`/`top` are relative to. Read
// explicitly rather than off the cell's ancestors — the clamp measures against
// this element, and an explicit ref does not rely on the markup between the two
// staying the way it is today.
const gridWrapper = useTemplateRef<HTMLElement>('gridWrapper')

// Bumped on every hover, so the clamp below can tell whether the pointer has
// moved on since it started. Comparing `hovered.value` to the cell would not
// work: a `ref` hands back a reactive proxy, never the object that was put in.
let hoverToken = 0

async function showTooltip(cell: Cell, event: MouseEvent) {
  const target = event.currentTarget as HTMLElement
  const wrapper = gridWrapper.value
  if (!wrapper) return

  const token = ++hoverToken
  const centre = target.offsetLeft + target.offsetWidth / 2
  hovered.value = cell
  tooltipAnchor.value = {
    x: centre,
    y: target.offsetTop,
    height: target.offsetHeight,
    above: target.offsetTop > 0,
  }

  await nextTick()
  // The pointer may have left (or moved on) while the panel was rendering.
  if (hoverToken !== token) return
  const half = (tooltipEl.value?.offsetWidth ?? 0) / 2
  tooltipAnchor.value = {
    x: Math.min(Math.max(centre, half), Math.max(wrapper.clientWidth - half, half)),
    y: target.offsetTop,
    height: target.offsetHeight,
    above: target.offsetTop > 0,
  }
}

function hideTooltip() {
  hoverToken++
  hovered.value = null
  tooltipAnchor.value = null
}

const summary = computed(() => {
  const current = series.value
  if (!current || current.games === 0) return null
  // Same wins-over-games shape as the tooltips, so the total and a cell are read
  // the same way rather than in two different notations one card apart.
  return {
    rate: current.winRate === null ? null : `${Math.round(current.winRate * 100)}%`,
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
      <!-- Segmented control rather than four loose buttons: the views are one
           choice, and the shared inset strip is what says so. -->
      <div class="flex gap-0.5 rounded-md bg-elevated p-0.5">
        <button
          v-for="option in MODES"
          :key="option.key"
          type="button"
          class="rounded px-2 py-1 text-xs font-medium transition-colors"
          :class="mode === option.key
            ? 'bg-accented text-highlighted'
            : 'text-dimmed hover:text-default'"
          @click="() => { mode = option.key; hideTooltip() }"
        >
          {{ option.label }}
        </button>
      </div>
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

        <!-- The bottom padding is the room the hover panel needs: cells in the
             first row have nothing above them to sit over (the summary line is
             right there), so their panel flips below the tile. -->
        <div ref="gridWrapper" class="relative pb-6" @mouseleave="hideTooltip">
          <!-- One element per cell: a square, and nothing under it. -->
          <ul class="grid" :style="gridStyle">
            <li
              v-for="cell in cells"
              :key="cell.bucket.key"
              class="aspect-square rounded-[3px] transition-[filter,box-shadow] duration-100 hover:shadow-[0_0_0_2px_var(--ui-bg-elevated),0_0_0_3px_rgba(255,255,255,0.35)] hover:brightness-110"
              :style="{ backgroundColor: cell.fill }"
              :aria-label="`${cell.label} — ${cell.result}`"
              @mouseenter="showTooltip(cell, $event)"
            />
          </ul>

          <!-- Flipped below the tile for the first row, above it for any row
               that has one over it. -->
          <div
            v-if="hovered && tooltipAnchor"
            ref="tooltipEl"
            role="tooltip"
            class="pointer-events-none absolute z-10 -translate-x-1/2 whitespace-nowrap rounded-md border border-default bg-elevated px-2 py-1 text-xs leading-tight tabular-nums shadow-lg"
            :class="tooltipAnchor.above ? '-translate-y-full' : 'translate-y-0'"
            :style="{
              left: `${tooltipAnchor.x}px`,
              top: tooltipAnchor.above
                ? `${tooltipAnchor.y - 6}px`
                : `${tooltipAnchor.y + tooltipAnchor.height + 6}px`,
            }"
          >
            <span class="font-medium text-default">{{ hovered.result }}</span>
            <span class="text-muted"> · {{ hovered.label }}</span>
          </div>
        </div>
      </template>
    </div>
  </section>
</template>

<script setup lang="ts">
import type { ActivityBucket, ActivityMode, TruemainActivityResponse } from '~~/shared/types/activity'
import {
  ACTIVITY_EMPTY_FILL,
  ACTIVITY_RAMP,
  activityBucketCaption,
  activityBucketLabel,
  activityBucketResult,
  activityCellFill,
  activityCellsAreGames,
  activityCoverageLabel,
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
 * **The card is a wide band, not a block.** It spans the profile column, so the
 * grid runs along it: one tile per day, stretched to use the width and wrapping
 * only when the row is genuinely full. A seven-row calendar was tried and
 * dropped — these windows are a fortnight long at most, so it stood as a narrow
 * tower in a wide card.
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
 * Per-window tile geometry, in pixels. `min` is the width below which the row
 * wraps, `max` the width a tile may grow to once `auto-fit` shares the row out.
 * The two calendar windows carry few enough cells to caption every one, so they
 * drop the square (`max: null`) for a band that spans the card; a day's games
 * are an uncaptioned strip of squares, capped so a two-game evening does not
 * render as two billboards.
 */
const GEOMETRY: Record<ActivityMode, { min: number, max: number | null, gap: number }> = {
  patch: { min: 30, max: null, gap: 6 },
  week: { min: 44, max: null, gap: 8 },
  day: { min: 14, max: 26, gap: 3 },
}

// Patch is the default: it is the widest window the retained data can back, and
// the one an idle cell carries the most meaning in.
const mode = ref<ActivityMode>('patch')

const series = computed(() => props.data?.[mode.value] ?? null)

const maxGames = computed(() => (series.value ? activityMaxGames(series.value) : 0))

const geometry = computed(() => GEOMETRY[mode.value])

interface Cell {
  bucket: ActivityBucket
  fill: string
  label: string
  caption: string | null
  result: string
  empty: boolean
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
      caption: activityBucketCaption(bucket, current.mode),
      result: activityBucketResult(bucket),
      empty: fill === null,
    }
  })
})

const captioned = computed(() => cells.value.some(cell => cell.caption !== null))

/**
 * The grid track definition. `auto-fit` collapses the tracks no cell landed in,
 * so the tiles share the whole width instead of hugging the left edge, and
 * `max-width` caps how large that can make them — a two-game evening is a pair
 * of squares, not two billboards.
 */
const gridStyle = computed(() => {
  const { min, max, gap } = geometry.value
  return {
    gap: `${gap}px`,
    gridTemplateColumns: `repeat(auto-fit, minmax(${min}px, 1fr))`,
    maxWidth: max === null ? undefined : `${cells.value.length * (max + gap) - gap}px`,
  }
})

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

const coverage = computed(() => (series.value ? activityCoverageLabel(series.value) : null))

/**
 * The legend's steps, in the order the ramp draws them: pale for a quiet day,
 * deep for a busy one. The per-game window is the one place the ramp does not
 * mean volume — every cell there is a single game — so it names the two steps
 * that window actually draws instead of a four-step scale it never uses.
 */
const legend = computed(() => {
  if (perGame.value) {
    return {
      steps: [ACTIVITY_RAMP[1]!, ACTIVITY_RAMP[3]!],
      from: 'Lost',
      to: 'Won',
    }
  }
  return { steps: [...ACTIVITY_RAMP], from: 'Less', to: 'More' }
})

const showsEmptyCells = computed(() => cells.value.some(cell => cell.empty))

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
        <!-- Series total on the left, the period it speaks for on the right.
             Per-cell figures live in the hover panel, so this line stays put
             instead of flickering as the pointer crosses the grid. -->
        <div class="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
          <p v-if="summary" class="flex items-baseline gap-2 tabular-nums">
            <span v-if="summary.rate" class="text-lg font-semibold leading-none text-highlighted">
              {{ summary.rate }}
            </span>
            <span class="text-xs text-muted">{{ summary.record }} · {{ summary.games }}</span>
          </p>
          <p v-else class="text-xs text-muted">
            No games in this view.
          </p>
          <p class="text-xs text-dimmed tabular-nums">
            <!-- Which patch, when the window is one: the days on screen are its
                 days, and the number is the only thing that says so. -->
            <span v-if="series?.patch" class="text-muted">Patch {{ series.patch }}</span>
            <span v-if="series?.patch && coverage"> · </span>
            <span v-if="coverage">{{ coverage }}</span>
          </p>
        </div>

        <!-- The bottom padding is the room the hover panel needs: cells in the
             first row have nothing above them to sit over (the summary line is
             right there), so their panel flips below the tile. Reserving the
             space here is what keeps it off the legend. -->
        <div ref="gridWrapper" class="relative pb-6" @mouseleave="hideTooltip">
          <ul class="grid" :style="gridStyle">
            <li
              v-for="cell in cells"
              :key="cell.bucket.key"
              class="flex flex-col gap-1"
            >
              <div
                class="w-full rounded-[3px] transition-[filter,box-shadow] duration-100 hover:shadow-[0_0_0_2px_var(--ui-bg-elevated),0_0_0_3px_rgba(255,255,255,0.35)] hover:brightness-110"
                :class="captioned ? 'h-10 rounded' : 'aspect-square'"
                :style="{ backgroundColor: cell.fill }"
                :aria-label="`${cell.label} — ${cell.result}`"
                @mouseenter="showTooltip(cell, $event)"
              />
              <!-- Only the calendar windows get captions; a day's games are read
                   as a strip, and a time under each turns it into a table. -->
              <span
                v-if="cell.caption"
                class="truncate text-center text-[10px] leading-none text-dimmed tabular-nums"
              >
                {{ cell.caption }}
              </span>
            </li>
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

        <!-- Legend. One ramp, one meaning: how much was played. The idle tile is
             named too — it is the tile the reader has to be able to tell from
             the faintest played one. -->
        <div class="flex flex-wrap items-center gap-x-4 gap-y-2 text-[10px] uppercase tracking-wide text-dimmed">
          <span v-if="showsEmptyCells" class="flex items-center gap-1.5">
            <span class="size-2.5 rounded-[2px]" :style="{ backgroundColor: ACTIVITY_EMPTY_FILL }" />
            No games
          </span>
          <span class="ml-auto flex items-center gap-1.5">
            {{ legend.from }}
            <span class="flex gap-0.5">
              <span
                v-for="step in legend.steps"
                :key="step"
                class="size-2.5 rounded-[2px]"
                :style="{ backgroundColor: step }"
              />
            </span>
            {{ legend.to }}
          </span>
        </div>
      </template>
    </div>
  </section>
</template>

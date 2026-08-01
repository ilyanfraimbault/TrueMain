<script setup lang="ts">
import type { ActivityBucket, ActivityMode, TruemainActivityResponse } from '~~/shared/types/activity'
import {
  activityBucketLabel,
  activityBucketResult,
  activityCellFill,
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
 * - **An empty period is not a lost one.** A cell with no games gets its own
 *   faint tile, the way GitHub draws an idle day, while a cell whose games were
 *   all losses gets a muted rose-grey fill. An outline alone dissolved into the
 *   glass surface, so the empty state is painted — but barely: punched out as a
 *   dark hole it read as a hard-edged gap in the card rather than as a lull.
 */
const props = withDefaults(defineProps<{
  data: TruemainActivityResponse | null
  loading?: boolean
}>(), {
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

/*
 * Hover tooltip. A `UTooltip` per cell would mount sixty poppers for one grid,
 * so this is a single panel moved to whichever square the pointer is on —
 * anchored inside the grid, which is `relative` for exactly that reason.
 *
 * The position is taken from the cell's own offsets rather than from
 * `getBoundingClientRect`, so scrolling the page cannot desync the panel from
 * its square. It is clamped to the grid after the panel has rendered, because
 * clamping needs its measured width: a square at either edge would otherwise
 * push the panel past the card.
 */
const hovered = ref<Cell | null>(null)
const tooltipAnchor = ref<{ x: number, y: number, above: boolean } | null>(null)
const tooltipEl = useTemplateRef<HTMLElement>('tooltipEl')
// The positioned ancestor the tooltip's `left`/`top` are relative to. Read
// explicitly rather than via the cell's `parentElement` (the `<ul>`) — that
// happens to share the wrapper's width today, but only because nothing else
// sits beside the grid; an explicit ref doesn't rely on that staying true.
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
  tooltipAnchor.value = { x: centre, y: target.offsetTop, above: target.offsetTop > 0 }

  await nextTick()
  // The pointer may have left (or moved on) while the panel was rendering.
  if (hoverToken !== token) return
  const half = (tooltipEl.value?.offsetWidth ?? 0) / 2
  // Cells in the first row have no room above them for the panel to sit in —
  // the card has no `overflow-hidden` to catch it, so it would spill over the
  // mode buttons above the grid. Flip it below the cell there instead.
  tooltipAnchor.value = {
    x: Math.min(Math.max(centre, half), Math.max(wrapper.clientWidth - half, half)),
    y: target.offsetTop,
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
  if (!current) return null
  if (current.games === 0) return null
  const rate = current.winRate === null ? null : `${Math.round(current.winRate * 100)}%`
  // Same wins-over-games shape as the tooltips, so the total and a cell are read
  // the same way rather than in two different notations one card apart.
  return rate
    ? `${current.wins}/${current.games} games · ${rate}`
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
          @click="() => { mode = option.key; hideTooltip() }"
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
        <!-- Series total. Per-cell figures live in the hover tooltip, so this
             line stays put instead of flickering as the pointer crosses. -->
        <p class="min-h-4 truncate text-xs text-muted tabular-nums">
          {{ summary }}
        </p>

        <p v-if="isEmpty" class="py-2 text-sm text-muted">
          Nothing to plot for this view.
        </p>

        <!-- Fixed 11 px squares packed left, GitHub-contribution style: the cell
             size is the same in every mode and at every width, so the grid just
             wraps onto more rows instead of inflating into big tiles. -->
        <div v-else ref="gridWrapper" class="relative" @mouseleave="hideTooltip">
          <ul class="grid grid-cols-[repeat(auto-fill,11px)] gap-[3px]">
            <li
              v-for="cell in cells"
              :key="cell.bucket.key"
              class="size-[11px] rounded-[2px] transition-colors"
              :class="cell.fill
                ? 'ring-1 ring-inset ring-white/5'
                : 'bg-white/6 ring-1 ring-inset ring-white/8'"
              :style="cell.fill ? { backgroundColor: cell.fill } : undefined"
              :aria-label="`${cell.label} — ${cell.result}`"
              @mouseenter="showTooltip(cell, $event)"
            />
          </ul>

          <!-- The first row has no row above it to sit over, and the card has no
               `overflow-hidden` to catch an overflow — it would spill onto the
               mode buttons above the grid. Flip below the cell there instead. -->
          <div
            v-if="hovered && tooltipAnchor"
            ref="tooltipEl"
            role="tooltip"
            class="glass pointer-events-none absolute z-10 -translate-x-1/2 whitespace-nowrap rounded-md px-2 py-1 text-xs leading-tight tabular-nums shadow-lg"
            :class="tooltipAnchor.above ? '-translate-y-full' : 'translate-y-0'"
            :style="{
              left: `${tooltipAnchor.x}px`,
              top: tooltipAnchor.above ? `${tooltipAnchor.y - 6}px` : `${tooltipAnchor.y + 17}px`,
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

<script setup lang="ts">
import type { ActivityCell } from '#common/utils/activity-heatmap'

// The activity card's squares and their hover panel.
defineProps<{
  cells: ActivityCell[]
}>()

/**
 * Tile geometry, in pixels — one size for every window, because a square that
 * changed size with the switch would be one more thing the control silently
 * re-labels. `auto-fill` at a *fixed* track width (not `minmax(_, 1fr)`) is what
 * keeps them square and packed left instead of stretching to share the row.
 */
const TILE = 14
const GAP = 4

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
const hovered = ref<ActivityCell | null>(null)
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

async function showTooltip(cell: ActivityCell, event: MouseEvent) {
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

// The card closes the panel when its window switch is pressed.
defineExpose({ hideTooltip })
</script>

<template>
  <!-- The bottom padding is the room the hover panel needs: cells in the
       first row have nothing above them to sit over (the summary line is
       right there), so their panel flips below the tile. -->
  <div
    ref="gridWrapper"
    class="relative pb-6"
    @mouseleave="hideTooltip"
  >
    <!-- One element per cell: a square, and nothing under it. -->
    <ul
      class="grid"
      :style="gridStyle"
    >
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

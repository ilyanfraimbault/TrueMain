<script setup lang="ts">
import type { BuildItemSet } from '~~/shared/types/champions'
import type { StaticItemData } from '~~/shared/types/static-data'
import { itemSlots } from '~~/shared/utils/build'
import type { ItemContextCard } from '~~/shared/utils/item-context'
import { itemContextKey } from '~~/shared/utils/item-context'

const props = defineProps<{
  starter: BuildItemSet | null
  itemsMap: Record<number, StaticItemData>
  /** Situational verdicts (#1451), keyed by slot + item. Absent where the surface has no slice for them. */
  itemContext?: Map<string, ItemContextCard>
}>()

/** This block only ever asks about the `Starter` slot — the same id answers a different question in each. */
function contextFor(itemId: number): ItemContextCard | undefined {
  return props.itemContext?.get(itemContextKey('Starter', itemId))
}

// One slot per id in the build, resolved or not — see `itemSlots`. Keying the
// no-data state off the resolved list instead made a loaded build claim it had
// no starter for as long as the item map was in flight.
const items = computed(() => itemSlots(props.starter?.itemIds, props.itemsMap))
</script>

<template>
  <div>
    <h2 class="text-sm font-medium text-muted">
      Starter
    </h2>
    <!-- Fixed from sm: 3 items × 36 px + 2 gaps × 4 px = 116 px
         (--width-starter-items in main.css), 36 px tall. Width is capped at
         the 3-item worst case; height is pinned so the "no data" state
         occupies the same box without collapsing the row. Mobile stays
         fluid (w-full). -->
    <div class="mt-2 flex h-9 w-full shrink-0 items-center gap-1 overflow-hidden sm:w-starter-items">
      <GameTooltipItemIcon
        v-for="(slot, index) in items"
        :key="`starter-${slot.id}-${index}`"
        :item="slot.item"
        :context="contextFor(slot.id)"
        :width="36"
        :height="36"
        class="size-9 shrink-0 rounded"
      />
      <span
        v-if="!items.length"
        class="text-sm text-muted"
      >
        No data
      </span>
    </div>
  </div>
</template>

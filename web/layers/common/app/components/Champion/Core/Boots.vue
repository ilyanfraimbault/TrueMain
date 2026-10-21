<script setup lang="ts">
import type { BuildItemSet } from '#shared/types/champions'
import type { StaticItemData } from '#shared/types/static-data'
import { itemSlots } from '#shared/utils/build'
import type { ItemContextCard } from '#shared/utils/item-context'
import { resolveItemContext } from '#shared/utils/item-context'

const props = defineProps<{
  boots: BuildItemSet | null
  itemsMap: Record<number, StaticItemData>
  /** Situational verdicts (#1451), keyed by slot + item. Absent where the surface has no slice for them. */
  itemContext?: Map<string, ItemContextCard>
}>()

/** This block only ever asks about the `Boots` slot — the same id answers a different question in each. */
function contextFor(itemId: number): ItemContextCard | undefined {
  return resolveItemContext(props.itemContext, 'Boots', itemId)
}

// One slot per id in the build, resolved or not — see `itemSlots`. Keying the
// no-data state off the resolved list instead made a loaded build claim it had
// no boots for as long as the item map was in flight.
const items = computed(() => itemSlots(props.boots?.itemIds, props.itemsMap))
</script>

<template>
  <div>
    <h2 class="text-sm font-medium text-muted">
      Boots
    </h2>
    <!-- At least 2 items × 36 px + 1 gap × 4 px = 76 px wide from sm, 36 px tall.
         A build's boots rarely have more than 1 item, but 2 is its realistic
         max; a desktop kit lists up to 4 boots to pick between (#2185), and
         the box grows for them. Height is pinned so the "no data" state
         occupies the same box. Mobile stays fluid (w-full). -->
    <div class="mt-2 flex h-9 w-full shrink-0 items-center gap-1 overflow-hidden sm:w-auto sm:min-w-[76px]">
      <GameTooltipItemIcon
        v-for="(slot, index) in items"
        :key="`boots-${slot.id}-${index}`"
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

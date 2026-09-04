<script setup lang="ts">
import type { BuildItemPath } from '~~/shared/types/champions'
import type { StaticItemData } from '~~/shared/types/static-data'
import { itemSlots } from '~~/shared/utils/build'
import type { ItemContextCard } from '~~/shared/utils/item-context'
import { itemContextKey } from '~~/shared/utils/item-context'

const props = defineProps<{
  path: BuildItemPath | null
  itemsMap: Record<number, StaticItemData>
  /** Situational verdicts (#1451), keyed by slot + item. Absent where the surface has no slice for them. */
  itemContext?: Map<string, ItemContextCard>
}>()

/** This block only ever asks about the `Build` slot — the same id answers a different question in each. */
function contextFor(itemId: number): ItemContextCard | undefined {
  return props.itemContext?.get(itemContextKey('Build', itemId))
}

// One slot per id in the build, resolved or not — see `itemSlots`. Keying the
// no-data state off the resolved list instead made a loaded build claim it had
// no path for as long as the item map was in flight.
const items = computed(() => itemSlots(props.path?.itemIds, props.itemsMap))
</script>

<template>
  <!-- Fixed from sm: 6 items × 36 px + 5 chevrons × 16 px + 10 gaps × 4 px = 336 px
       (--width-build-path in main.css), 36 px tall. Width locks at the 6-item
       worst case; height is pinned so no-data state doesn't collapse the row.
       On mobile (< sm) the fixed width is removed and items can wrap naturally
       inside available width. justify-center keeps a short chain centred in
       its parent's A2 area. -->
  <div class="flex flex-col items-center">
    <h2 class="text-sm font-medium text-muted">
      Build path
    </h2>
    <div class="mt-2 flex h-9 items-center justify-center gap-1 overflow-hidden sm:w-build-path">
      <template
        v-for="(slot, index) in items"
        :key="`bp-${slot.id}-${index}`"
      >
        <GameTooltipItemIcon
          :item="slot.item"
          :context="contextFor(slot.id)"
          :width="36"
          :height="36"
          class="size-9 shrink-0 rounded"
        />
        <UIcon
          v-if="index < items.length - 1"
          name="i-lucide-chevron-right"
          class="size-4 shrink-0 text-dimmed"
        />
      </template>
      <!-- Same wording as the Boots / Starter blocks in the same row. Left
           blank, this box read as "still loading" next to two siblings that
           say what they mean. -->
      <span
        v-if="!items.length"
        class="text-sm text-muted"
      >
        No data
      </span>
    </div>
  </div>
</template>

<script setup lang="ts">
import type { StaticItemData } from '~~/shared/types/static-data'
import { isNonBuildItem } from '~~/shared/utils/build'

// A player's end-of-game inventory, shared by the collapsed match row and the
// expanded scoreboard, laid out the way the game lays it out: inventory slots
// 0..5 as a 3×2 grid in slot order, then a side column stacking the trinket
// over Riot's role-bound slot. That slot sits outside the six — it is where a
// bot laner's boots go once the role quest is done (six items *and* boots), and
// where the other roles' quest reward goes — so nothing is pulled out of or
// packed into the grid.
const props = withDefaults(defineProps<{
  /** Inventory slots 0..5 as the API sends them (0 = empty). */
  itemIds: number[]
  trinketItemId: number
  /** Riot's role-bound slot (0 = empty, or a row ingested before it was recorded). */
  roleBoundItemId?: number
  items: Record<number, StaticItemData>
  /** Icon edge in px — drives every cell through an inline style, so empty slots and icons always match. */
  size?: number
}>(), {
  roleBoundItemId: 0,
  size: 24,
})

// Three-state slot so the grid tells apart a slot the player never filled
// (placeholder) from one whose static data hasn't resolved yet (id > 0 but no
// entry in the map — the icon keeps its skeleton, a real loading state).
type InventorySlot =
  | { kind: 'empty' }
  | { kind: 'loading' }
  | { kind: 'item', item: StaticItemData }

const INVENTORY_SLOT_COUNT = 6

function toSlot(id: number | undefined): InventorySlot {
  if (!id || id <= 0 || isNonBuildItem(id)) return { kind: 'empty' }
  const item = props.items[id]
  return item ? { kind: 'item', item } : { kind: 'loading' }
}

const inventory = computed<InventorySlot[]>(() =>
  Array.from({ length: INVENTORY_SLOT_COUNT }, (_, slot) => toSlot(props.itemIds[slot])),
)

// The Eye of the Herald — a Rift Herald summon parked in the trinket slot,
// never an item the player chose — reads as an empty trinket.
const trinket = computed(() => toSlot(props.trinketItemId))
const roleBound = computed(() => toSlot(props.roleBoundItemId))

const cellStyle = computed(() => ({ width: `${props.size}px`, height: `${props.size}px` }))
const gap = computed(() => (props.size >= 24 ? 'gap-1' : 'gap-0.5'))
</script>

<template>
  <div
    class="flex shrink-0 items-center rounded-lg bg-black/25 ring-1 ring-white/5"
    :class="[gap, size >= 24 ? 'p-1.5' : 'p-0.5']"
  >
    <div class="grid grid-cols-3" :class="gap">
      <template v-for="(slot, idx) in inventory" :key="`item-${idx}`">
        <div
          v-if="slot.kind === 'empty'"
          class="shrink-0 rounded bg-white/5"
          :style="cellStyle"
          aria-hidden="true"
        />
        <GameTooltipItemIcon
          v-else
          :item="slot.kind === 'item' ? slot.item : null"
          :width="size"
          :height="size"
          loading="lazy"
          class="rounded"
        />
      </template>
    </div>
    <div class="flex flex-col" :class="gap">
      <GameTooltipItemIcon
        v-if="trinket.kind !== 'empty'"
        :item="trinket.kind === 'item' ? trinket.item : null"
        :width="size"
        :height="size"
        loading="lazy"
        class="rounded-full"
      />
      <div v-else class="shrink-0 rounded-full bg-white/5" :style="cellStyle" aria-hidden="true" />
      <GameTooltipItemIcon
        v-if="roleBound.kind !== 'empty'"
        :item="roleBound.kind === 'item' ? roleBound.item : null"
        :width="size"
        :height="size"
        loading="lazy"
        class="rounded"
      />
      <div v-else class="shrink-0" :style="cellStyle" aria-hidden="true" />
    </div>
  </div>
</template>

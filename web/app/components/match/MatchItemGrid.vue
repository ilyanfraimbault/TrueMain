<script setup lang="ts">
import type { StaticItemData } from '~~/shared/types/static-data'
import { isBootsItem, isNonBuildItem } from '~~/shared/utils/build'

// A player's end-of-game inventory, shared by the collapsed match row and the
// expanded scoreboard so both read the same way: the non-boots items as a 3×2
// grid, then a trailing column stacking the trinket over the boots (scoreboard
// convention). Pulling the boots out is what makes the seventh inventory slot
// fit — a bot laner's role quest grants boots on top of six full items, so a
// fixed six-slot strip silently dropped one of them.
const props = withDefaults(defineProps<{
  /** Inventory slots as the API sends them (0 = empty). */
  itemIds: number[]
  trinketItemId: number
  items: Record<number, StaticItemData>
  /** Icon edge in px — drives every cell through an inline style, so empty slots and icons always match. */
  size?: number
}>(), {
  size: 24,
})

// Three-state slot so the grid tells apart a slot the player never bought
// (invisible placeholder) from one whose static data hasn't resolved yet (id > 0
// but no entry in the map — the icon keeps its skeleton, a real loading state).
type InventorySlot =
  | { kind: 'empty' }
  | { kind: 'loading' }
  | { kind: 'item', item: StaticItemData }

const INVENTORY_SLOT_COUNT = 6

const bootsItem = computed<StaticItemData | null>(() => {
  for (const id of props.itemIds) {
    const item = props.items[id]
    if (item && isBootsItem(item)) return item
  }
  return null
})

// Real items are left-aligned and the remaining cells become placeholders, so
// the grid keeps the same width on every row and the trinket column lines up
// down the list. The Eye of the Herald — a Rift Herald summon, never a build
// item — is dropped.
const inventory = computed<InventorySlot[]>(() => {
  const bootsId = bootsItem.value?.id
  const filled: InventorySlot[] = props.itemIds
    .filter(id => id > 0 && id !== bootsId && !isNonBuildItem(id))
    .map((id) => {
      const item = props.items[id]
      return item ? { kind: 'item', item } : { kind: 'loading' }
    })
  const empties: InventorySlot[] = Array.from(
    { length: Math.max(0, INVENTORY_SLOT_COUNT - filled.length) },
    () => ({ kind: 'empty' }),
  )
  return [...filled, ...empties]
})

const trinket = computed<StaticItemData | null>(() => {
  const id = props.trinketItemId
  if (id <= 0 || isNonBuildItem(id)) return null
  return props.items[id] ?? null
})

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
        v-if="trinket"
        :item="trinket"
        :width="size"
        :height="size"
        loading="lazy"
        class="rounded-full"
      />
      <div v-else class="shrink-0 rounded-full bg-white/5" :style="cellStyle" aria-hidden="true" />
      <GameTooltipItemIcon
        v-if="bootsItem"
        :item="bootsItem"
        :width="size"
        :height="size"
        loading="lazy"
        class="rounded"
      />
      <div v-else class="shrink-0" :style="cellStyle" aria-hidden="true" />
    </div>
  </div>
</template>

<script setup lang="ts">
import type { StaticItemData } from '#shared/types/static-data'

/**
 * A row of the kit editor's item slots — the starter basket, the boots: each
 * item a button that asks for another, with a × to drop it, then a "+" slot
 * while the row has room.
 */
const props = defineProps<{
  itemIds: number[]
  items: Record<number, StaticItemData>
  max: number
  /** The "+" slot's label: what it adds. */
  addLabel: string
}>()

const emit = defineEmits<{
  /** Asks for an item: to replace the one at `index`, or a new one for `null`. */
  pick: [index: number | null]
  remove: [index: number]
}>()

const name = (itemId: number) => props.items[itemId]?.name ?? `Item ${itemId}`
</script>

<template>
  <div class="flex gap-1.5">
    <div v-for="(itemId, index) in itemIds" :key="`${itemId}-${index}`" class="group relative">
      <button type="button" class="flex rounded transition hover:brightness-125 focus-visible:outline-2 focus-visible:outline-primary" :aria-label="`Replace ${name(itemId)}`" @click="emit('pick', index)">
        <GameTooltipItemIcon :item="items[itemId] ?? null" :width="36" :height="36" class="size-9 rounded" />
      </button>
      <button type="button" class="absolute -right-1.5 -top-1.5 hidden size-4 items-center justify-center rounded-full bg-elevated text-dimmed ring-1 ring-default hover:text-error group-hover:flex group-focus-within:flex" :aria-label="`Remove ${name(itemId)}`" @click="emit('remove', index)">
        <UIcon name="i-lucide-x" class="size-3" />
      </button>
    </div>
    <button v-if="itemIds.length < max" type="button" class="flex size-9 items-center justify-center rounded border border-dashed border-default text-dimmed transition hover:border-primary/60 hover:text-primary focus-visible:outline-2 focus-visible:outline-primary" :aria-label="addLabel" :title="addLabel" @click="emit('pick', null)">
      <UIcon name="i-lucide-plus" class="size-4" />
    </button>
  </div>
</template>

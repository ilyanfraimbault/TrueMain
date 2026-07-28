<script setup lang="ts">
import type { BuildItemSet } from '~~/shared/types/champions'
import type { StaticItemData } from '~~/shared/types/static-data'
import { formatPercentage } from '~~/shared/utils/ddragon'

/**
 * Items bought in the sampled games outside the core path, ranked by the same
 * similarity-and-win weighting as the rest of the recommendation. The API has
 * always returned them; the builder never rendered them until #921, where they
 * carry real weight — matchup-specific answers (an early Executioner's, a
 * defensive third item) live here rather than in the core path.
 *
 * Each set holds a single item id; anything unresolved in the item map is
 * dropped rather than rendered as a blank tile.
 */
const props = defineProps<{
  items: BuildItemSet[]
  itemsMap: Record<number, StaticItemData>
}>()

const entries = computed(() =>
  props.items
    .map(set => ({ set, item: set.itemIds[0] === undefined ? undefined : props.itemsMap[set.itemIds[0]] }))
    .filter((entry): entry is { set: BuildItemSet, item: StaticItemData } => Boolean(entry.item)))
</script>

<template>
  <div v-if="entries.length > 0">
    <h3 class="text-sm font-medium text-muted">
      Situational picks
    </h3>
    <p class="mt-0.5 text-xs text-dimmed">
      Bought outside the core path in these games.
    </p>
    <ul class="mt-2 flex flex-wrap gap-3">
      <li
        v-for="entry in entries"
        :key="entry.item.id"
        class="flex w-16 flex-col items-center gap-1"
      >
        <GameTooltipItemIcon
          :item="entry.item"
          :width="36"
          :height="36"
          class="size-9 shrink-0 rounded"
        />
        <span class="text-xs text-muted">
          {{ formatPercentage(entry.set.pickRate, 0) }}
        </span>
        <span class="text-xs text-dimmed">
          {{ formatPercentage(entry.set.winRate, 0) }} win
        </span>
      </li>
    </ul>
  </div>
</template>

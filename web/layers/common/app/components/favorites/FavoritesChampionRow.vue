<script setup lang="ts">
import type { ChampionStaticListItem } from '#shared/types/static-data'
import type { FavoriteChampion } from '#common/utils/favorites'
import { POSITION_BY_VALUE } from '#common/utils/positions'
import { championDisplayName } from '#shared/utils/champion-name'

// One starred champion on the favorites page (#2080): a compact row — the
// champion, the position's icon — that opens its page on that position, and the
// star to drop it. Drawn from the static champion list alone, no fetch per row.
const props = defineProps<{
  favorite: FavoriteChampion
  championsById: ReadonlyMap<number, ChampionStaticListItem>
}>()

const { pathFor } = useChampionSlugs()

const champion = computed(() => props.championsById.get(props.favorite.championId) ?? null)
const name = computed(() => championDisplayName(props.favorite.championId, champion.value))
const position = computed(() => POSITION_BY_VALUE.get(props.favorite.position ?? '') ?? null)
const label = computed(() => [name.value, position.value?.label].filter(Boolean).join(' '))

const to = computed(() => ({
  path: pathFor(props.favorite.championId),
  query: props.favorite.position ? { position: props.favorite.position } : {},
}))
</script>

<template>
  <div class="surface flex items-center gap-2 rounded-lg p-1.5">
    <NuxtLink :to="to" class="surface-hover flex min-w-0 flex-1 items-center gap-2.5 rounded-md p-1">
      <SkeletonImage
        :src="champion?.iconUrl"
        :alt="''"
        :width="32"
        :height="32"
        class="size-8 shrink-0 rounded-md"
      />
      <span class="min-w-0 flex-1 truncate text-sm font-medium text-highlighted">{{ name }}</span>
      <span v-if="position" :title="position.label" class="inline-flex shrink-0">
        <SkeletonImage
          :src="position.iconUrl"
          :alt="position.label"
          transparent
          :width="16"
          :height="16"
          class="size-4"
        />
      </span>
    </NuxtLink>
    <FavoriteToggle :champion="favorite" :champion-label="label" />
  </div>
</template>

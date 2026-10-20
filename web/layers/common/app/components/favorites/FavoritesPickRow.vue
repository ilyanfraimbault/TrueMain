<script setup lang="ts">
import type { ChampionStaticListItem } from '#shared/types/static-data'
import type { FavoritePick } from '#common/utils/favorites'
import { favoritePickQuery } from '#common/utils/favorites'
import { POSITION_BY_VALUE } from '#common/utils/positions'
import { championDisplayName } from '#shared/utils/champion-name'

// One starred champion or matchup on the favorites page (#2080): a compact row
// that opens the page it was starred on, and the star to drop it. Drawn from the
// static champion list alone — no fetch per row.
const props = defineProps<{
  pick: FavoritePick
  championsById: ReadonlyMap<number, ChampionStaticListItem>
}>()

const { pathFor } = useChampionSlugs()

const champion = computed(() => props.championsById.get(props.pick.championId) ?? null)
const opponent = computed(() => props.pick.kind === 'matchup'
  ? props.championsById.get(props.pick.opponentChampionId) ?? null
  : null)
const position = computed(() => POSITION_BY_VALUE.get(props.pick.position ?? '') ?? null)

const championName = computed(() => championDisplayName(props.pick.championId, champion.value))
const label = computed(() => props.pick.kind === 'matchup'
  ? `${championName.value} vs ${championDisplayName(props.pick.opponentChampionId, opponent.value)}`
  : championName.value)

const to = computed(() => ({
  path: props.pick.kind === 'matchup' ? '/matchup' : pathFor(props.pick.championId),
  query: favoritePickQuery(props.pick),
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
      <template v-if="pick.kind === 'matchup'">
        <span class="text-xs font-medium text-dimmed">vs</span>
        <SkeletonImage
          :src="opponent?.iconUrl"
          :alt="''"
          :width="32"
          :height="32"
          class="size-8 shrink-0 rounded-md ring-1 ring-accented"
        />
      </template>
      <span class="min-w-0 flex-1 leading-tight">
        <span class="block truncate text-sm font-medium text-highlighted">{{ label }}</span>
        <span class="block truncate text-xs text-muted">{{ position?.label ?? 'All positions' }}</span>
      </span>
    </NuxtLink>
    <FavoriteToggle :pick="pick" :pick-label="label" />
  </div>
</template>

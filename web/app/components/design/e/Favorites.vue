<script setup lang="ts">
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { championDisplayName } from '~~/shared/utils/champion-name'
import { POSITION_BY_VALUE } from '#shared/utils/positions'

// Variant E home, round 3: the visitor's starred champions, one click away.
// Read from localStorage after mount (`useFavoriteChampions`), so nothing shows
// on the server render and there is no hydration mismatch; absent when empty.
const props = defineProps<{
  championsById: Map<number, ChampionStaticListItem>
}>()

const { champions, championsHydrated } = useFavoriteChampions()
const { pathFor } = useChampionSlugs()

const chips = computed(() => champions.value.slice(0, 8).map((fav) => {
  const champ = props.championsById.get(fav.championId)
  return {
    key: `${fav.championId}-${fav.position ?? 'all'}`,
    to: { path: pathFor(fav.championId), query: fav.position ? { position: fav.position } : {} },
    name: championDisplayName(fav.championId, champ),
    iconUrl: champ?.iconUrl ?? '',
    lane: fav.position ? POSITION_BY_VALUE.get(fav.position) : undefined,
  }
}))
</script>

<template>
  <nav
    v-if="championsHydrated && chips.length"
    class="e-hx-favs"
    aria-label="Your champions"
  >
    <span class="e-hx-favs__label">
      <UIcon
        name="i-lucide-star"
        class="size-3.5"
      />
      Your champions
    </span>
    <NuxtLink
      v-for="chip in chips"
      :key="chip.key"
      :to="chip.to"
      class="e-hx-fav"
    >
      <img
        v-if="chip.iconUrl"
        :src="chip.iconUrl"
        alt=""
        width="22"
        height="22"
        class="size-[22px] rounded-md"
      >
      {{ chip.name }}
      <img
        v-if="chip.lane?.iconUrl"
        :src="chip.lane.iconUrl"
        :alt="chip.lane.label"
        width="14"
        height="14"
        class="size-3.5 opacity-70"
      >
    </NuxtLink>
  </nav>
</template>

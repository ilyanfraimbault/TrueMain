<script setup lang="ts">
import type { ChampionOverviewReadModel as ChampionOverviewResponse } from '#shared/contract/api'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'

// The patch's top rows (`GET /champions/overview`, tier first — a presence
// ranking, so it is labelled "most played and banned", never "strongest") as a
// wall of splash cards. Hovering one dims the rest: the light is on the card you
// point at. The real grid mounts only once the data is in, so its staggered
// reveal plays on the cards rather than on their skeletons.
const props = defineProps<{
  overview: ChampionOverviewResponse | null | undefined
  pending: boolean
  championsById: Map<number, ChampionStaticListItem>
}>()

const rows = computed(() => props.overview?.topRows ?? [])
const SKELETONS = 8
</script>

<template>
  <section
    class="b-section"
    aria-labelledby="b-champs-title"
  >
    <DesignBSectionHead
      id="b-champs-title"
      eyebrow="Tier list"
    >
      Most played and banned this patch.
      <template #lede>
        The champions every lobby is picking or banning, lane by lane, with their win, pick and ban rates.
      </template>
      <template #aside>
        <NuxtLink
          to="/champions/tierlist"
          class="b-link-arrow"
        >
          Full tier list
          <UIcon
            name="i-lucide-arrow-right"
            class="size-4"
          />
        </NuxtLink>
      </template>
    </DesignBSectionHead>

    <ul
      v-if="pending"
      class="b-champs"
      aria-hidden="true"
    >
      <li
        v-for="i in SKELETONS"
        :key="i"
      >
        <div class="b-champ b-champ--skeleton" />
      </li>
    </ul>
    <ul
      v-else-if="rows.length > 0"
      v-reveal.stagger="{ step: 70, effect: 'rise' }"
      class="b-champs"
    >
      <li
        v-for="(row, index) in rows"
        :key="`${row.championId}-${row.position}`"
      >
        <DesignBChampionCard
          :row="row"
          :rank="index + 1"
          :champion="championsById.get(row.championId)"
        />
      </li>
    </ul>
    <UEmpty
      v-else
      icon="i-lucide-trending-up"
      description="No champion stats for this patch yet."
    />
  </section>
</template>

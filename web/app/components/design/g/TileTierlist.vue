<script setup lang="ts">
import type { ChampionOverviewRowReadModel as ChampionOverviewRow } from '#shared/contract/api'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { championDisplayName } from '~~/shared/utils/champion-name'

// The way into the tier list: the first champions of the patch's presence
// ranking, each wearing its tier letter. Under the cursor the portraits rise in
// turn, a ripple running left to right.
const props = defineProps<{
  rows: ChampionOverviewRow[]
  championsById: Map<number, ChampionStaticListItem>
  pending: boolean
  index: number
}>()

const COUNT = 8
const canonicalIcon = useCanonicalIcon()

const picks = computed(() => props.rows.slice(0, COUNT).map((row) => {
  const champion = props.championsById.get(row.championId)
  return {
    key: `${row.championId}-${row.position}`,
    name: championDisplayName(row.championId, champion),
    icon: canonicalIcon(champion?.iconUrl, 96),
    tier: row.tier,
  }
}))
</script>

<template>
  <DesignGTile
    area="tier"
    :index="index"
    to="/champions/tierlist"
    label="Tier list"
    icon="i-lucide-trending-up"
    class="g-tiertile"
  >
    <template #label-end>
      <span
        class="g-tile__go"
        aria-hidden="true"
      >
        <UIcon name="i-lucide-arrow-right" />
      </span>
    </template>

    <span
      v-if="pending"
      class="g-tiertile__row"
      aria-hidden="true"
    >
      <span
        v-for="i in COUNT"
        :key="i"
        class="g-tiertile__pick"
      >
        <span class="g-skel g-tiertile__icon" />
      </span>
    </span>
    <span
      v-else-if="picks.length"
      class="g-tiertile__row g-in"
    >
      <span
        v-for="(pick, i) in picks"
        :key="pick.key"
        class="g-tiertile__pick"
        :style="{ '--k': i }"
      >
        <img
          v-if="pick.icon"
          :src="pick.icon"
          :alt="pick.name"
          width="48"
          height="48"
          class="g-tiertile__icon img-skeleton"
        >
        <span
          v-else
          class="g-skel g-tiertile__icon"
        />
        <span class="g-tiertile__tier"><TierBadge :tier="pick.tier" /></span>
      </span>
    </span>
    <span
      v-else
      class="g-empty"
    >No tiers for this patch yet.</span>
    <span class="g-tiertile__cta">Open the tier list</span>
  </DesignGTile>
</template>

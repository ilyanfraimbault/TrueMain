<script setup lang="ts">
import type { ChampionOverviewRow } from '~~/shared/types/champions'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { championDisplayName } from '~~/shared/utils/champion-name'
import { formatCompactCount } from '~~/shared/utils/counts'
import { championLoadingUrlFromIcon, formatPercentage } from '~~/shared/utils/ddragon'
import { POSITION_BY_VALUE } from '#common/utils/positions'
import { winRateTone } from '#common/utils/rate-tone'

// One champion of the patch's top rows, on its loading-screen art. The card
// tilts toward the cursor (`v-tilt`), a glare follows it, the art and the text
// sit on separate depths so the tilt reads as parallax, and the edge lights up
// from the cursor (`data-b-spot`). Its three rates count up the first time the
// card is on screen.
const props = defineProps<{
  row: ChampionOverviewRow
  rank: number
  champion?: ChampionStaticListItem
}>()

const { pathFor } = useChampionSlugs()
const loadingArt = useCanonicalLoadingArt()

const name = computed(() => championDisplayName(props.row.championId, props.champion))
const art = computed(() => loadingArt(championLoadingUrlFromIcon(props.champion?.iconUrl)))
const lane = computed(() => POSITION_BY_VALUE.get(props.row.position))
const to = computed(() => ({ path: pathFor(props.row.championId), query: { position: props.row.position } }))
const rankLabel = computed(() => String(props.rank).padStart(2, '0'))

const pct = (value: number) => formatPercentage(value)
</script>

<template>
  <NuxtLink
    v-tilt="7"
    :to="to"
    class="b-champ"
    data-b-spot
  >
    <span
      class="b-champ__frame"
      aria-hidden="true"
    >
      <img
        v-if="art"
        :src="art"
        alt=""
        width="308"
        height="560"
        loading="lazy"
        decoding="async"
        class="b-champ__art img-skeleton"
      >
      <span class="b-champ__shade" />
      <span class="b-champ__glare" />
    </span>

    <span class="b-champ__top">
      <span
        class="b-champ__rank"
        aria-hidden="true"
      >{{ rankLabel }}</span>
      <TierBadge
        :tier="row.tier"
        stacked
      />
    </span>

    <span class="b-champ__body">
      <span class="b-champ__lane">
        <img
          v-if="lane?.iconUrl"
          :src="lane.iconUrl"
          alt=""
          width="14"
          height="14"
          class="size-3.5 opacity-80"
        >
        <span>{{ lane?.label ?? row.position }}</span>
        <span
          class="b-champ__dot"
          aria-hidden="true"
        />
        <span>{{ formatCompactCount(row.games) }} games</span>
      </span>
      <span class="b-champ__name">{{ name }}</span>
      <span class="b-champ__stats">
        <span class="b-champ__stat">
          <span
            class="b-champ__value"
            :class="winRateTone(row.winRate)"
          ><FxCount
            :value="row.winRate"
            :format="pct"
            :duration="1600"
          /></span>
          <span class="b-champ__label">Win</span>
        </span>
        <span class="b-champ__stat">
          <span class="b-champ__value"><FxCount
            :value="row.pickRate"
            :format="pct"
            :duration="1600"
          /></span>
          <span class="b-champ__label">Pick</span>
        </span>
        <span
          v-if="row.banRate !== null"
          class="b-champ__stat"
        >
          <span class="b-champ__value"><FxCount
            :value="row.banRate"
            :format="pct"
            :duration="1600"
          /></span>
          <span class="b-champ__label">Ban</span>
        </span>
      </span>
    </span>
  </NuxtLink>
</template>

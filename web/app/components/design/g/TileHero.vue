<script setup lang="ts">
import type { ChampionOverviewRow } from '~~/shared/types/champions'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { championDisplayName } from '~~/shared/utils/champion-name'
import { formatCount } from '~~/shared/utils/counts'
import { formatPercentage } from '~~/shared/utils/ddragon'
import { POSITION_BY_VALUE } from '#common/utils/positions'
import { winRateTone } from '#common/utils/rate-tone'

// The bento's anchor: the patch's most present champion (the overview's first
// row — a presence ranking, pick and ban rate leading) on its splash art. The
// art drifts against the cursor while it is over the tile, so the picture
// reads as a window with depth behind the glass; the three rates count up.
const props = defineProps<{
  row: ChampionOverviewRow | null
  champion?: ChampionStaticListItem
  patch: string | null
  pending: boolean
  index: number
}>()

const { pathFor } = useChampionSlugs()
const splash = useChampionSplash(() => (props.pending ? null : props.champion?.iconUrl))

const name = computed(() => (props.row ? championDisplayName(props.row.championId, props.champion) : ''))
const lane = computed(() => (props.row ? POSITION_BY_VALUE.get(props.row.position) : undefined))
const to = computed(() => (props.row && !props.pending
  ? { path: pathFor(props.row.championId), query: { position: props.row.position } }
  : '/champions/tierlist'))

const pct = (value: number) => formatPercentage(value)
</script>

<template>
  <DesignGTile
    area="hero"
    :index="index"
    :to="to"
    label="Most present"
    icon="i-lucide-flame"
  >
    <template #label-end>
      <span
        v-if="patch"
        class="g-tile__meta"
      >Patch {{ patch }}</span>
    </template>

    <template #art>
      <span
        class="g-hero__art"
        aria-hidden="true"
      >
        <DesignGArt
          :src="splash"
          :width="1280"
          :height="755"
          position="50% 22%"
          eager
        />
        <span class="g-hero__shade" />
      </span>
    </template>

    <span
      v-if="pending"
      class="g-hero__content"
      aria-hidden="true"
    >
      <span class="g-skel h-3 w-36" />
      <span class="g-skel mt-3 h-11 w-56" />
      <span class="g-hero__stats">
        <span class="g-skel h-9 w-16" />
        <span class="g-skel h-9 w-16" />
        <span class="g-skel h-9 w-16" />
      </span>
    </span>

    <span
      v-else-if="row"
      class="g-hero__content g-in"
    >
      <span class="g-hero__tier">
        <TierBadge
          :tier="row.tier"
          stacked
        />
      </span>
      <span class="g-hero__lane">
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
          class="g-dot"
          aria-hidden="true"
        />
        <span>{{ formatCount(row.games) }} games</span>
      </span>
      <span class="g-hero__name">{{ name }}</span>
      <span class="g-hero__stats">
        <span class="g-stat">
          <span
            class="g-stat__value"
            :class="winRateTone(row.winRate)"
          ><FxCount
            :value="row.winRate"
            :format="pct"
            :duration="1500"
          /></span>
          <span class="g-stat__label">Win rate</span>
        </span>
        <span class="g-stat">
          <span class="g-stat__value"><FxCount
            :value="row.pickRate"
            :format="pct"
            :duration="1500"
          /></span>
          <span class="g-stat__label">Pick rate</span>
        </span>
        <span
          v-if="row.banRate !== null"
          class="g-stat"
        >
          <span class="g-stat__value"><FxCount
            :value="row.banRate"
            :format="pct"
            :duration="1500"
          /></span>
          <span class="g-stat__label">Ban rate</span>
        </span>
      </span>
      <span
        class="g-hero__go"
        aria-hidden="true"
      >
        <UIcon name="i-lucide-arrow-up-right" />
      </span>
    </span>

    <span
      v-else
      class="g-hero__content g-empty"
    >No champion stats for this patch yet.</span>
  </DesignGTile>
</template>

<script setup lang="ts">
import type { ChampionOverviewRow } from '~~/shared/types/champions'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { championDisplayName } from '~~/shared/utils/champion-name'
import { championLoadingUrlFromIcon, formatPercentage } from '~~/shared/utils/ddragon'
import { POSITION_BY_VALUE } from '#shared/utils/positions'

// A champion card on its loading-screen art, for one superlative of the
// patch's most present rows: the most banned, or the highest win rate. The card
// tilts toward the cursor with a glare (the engine's `--g-u` / `--g-v`, no
// listener of its own). The banned one stands greyed and struck through, the
// way a ban reads in champion select, and gets its colour back under the cursor.
const props = defineProps<{
  kind: 'ban' | 'win'
  row: ChampionOverviewRow | null
  champion?: ChampionStaticListItem
  /** How many rows the superlative was picked from (the overview's slice). */
  poolSize: number
  pending: boolean
  index: number
}>()

const { pathFor } = useChampionSlugs()
const loadingArt = useCanonicalLoadingArt()

const art = computed(() => (props.pending ? null : loadingArt(championLoadingUrlFromIcon(props.champion?.iconUrl))))
const name = computed(() => (props.row ? championDisplayName(props.row.championId, props.champion) : ''))
const lane = computed(() => (props.row ? POSITION_BY_VALUE.get(props.row.position) : undefined))
const to = computed(() => (props.row && !props.pending
  ? { path: pathFor(props.row.championId), query: { position: props.row.position } }
  : '/champions/tierlist'))

const value = computed(() => {
  if (!props.row) return null
  return props.kind === 'ban' ? props.row.banRate : props.row.winRate
})

const copy = computed(() => (props.kind === 'ban'
  ? { label: 'Most banned', icon: 'i-lucide-ban', unit: 'ban rate' }
  : { label: 'Highest win rate', icon: 'i-lucide-chart-no-axes-column-increasing', unit: 'win rate' }))

const pct = (v: number) => formatPercentage(v)
</script>

<template>
  <DesignGTile
    :area="kind"
    :index="index"
    :to="to"
    :label="copy.label"
    :icon="copy.icon"
    class="g-card"
    :class="`g-card--${kind}`"
  >
    <template #art>
      <span
        class="g-card__art"
        aria-hidden="true"
      >
        <DesignGArt
          :src="art"
          :width="308"
          :height="560"
          position="50% 18%"
        />
        <span class="g-card__shade" />
        <span
          v-if="kind === 'ban'"
          class="g-card__strike"
        />
        <span class="g-card__glare" />
      </span>
    </template>

    <span
      v-if="pending"
      class="g-card__content"
      aria-hidden="true"
    >
      <span class="g-skel h-5 w-24" />
      <span class="g-skel mt-3 h-9 w-20" />
      <span class="g-skel mt-3 h-1 w-full" />
    </span>

    <span
      v-else-if="row && value !== null"
      class="g-card__content g-in"
    >
      <span class="g-card__lane">
        <img
          v-if="lane?.iconUrl"
          :src="lane.iconUrl"
          alt=""
          width="13"
          height="13"
          class="size-[13px] opacity-80"
        >
        {{ lane?.label ?? row.position }}
      </span>
      <span class="g-card__name">{{ name }}</span>
      <span class="g-card__figure">
        <FxCount
          :value="value"
          :format="pct"
          :duration="1500"
        />
      </span>
      <span
        class="g-meter"
        aria-hidden="true"
      >
        <span
          class="g-meter__fill"
          :style="{ '--fill': value }"
        />
        <span
          v-if="kind === 'win'"
          class="g-meter__mark"
        />
      </span>
      <span class="g-card__unit">{{ copy.unit }} · of the {{ poolSize }} most present</span>
    </span>

    <span
      v-else
      class="g-card__content g-empty"
    >Not measured this patch.</span>
  </DesignGTile>
</template>

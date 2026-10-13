<script setup lang="ts">
import type { RouteLocationRaw } from 'vue-router'
import type { ProfileMainChampion } from '#shared/types/profile'
import type { ChampionStaticListItem } from '#shared/types/static-data'
import { formatPercentage } from '#shared/utils/ddragon'
import { retiredSampleTooltip } from '#common/utils/retired-sample'

// The champions a player plays, most played first. On a profile each row reads
// the main analysis (count and play rate) and opens how THIS player builds the
// champion; the app's dashboard passes its own link and fills the `meta` and
// `value` slots with how the player fares on it (`DashboardChampionsCard`).
const props = withDefaults(defineProps<{
  mains: ProfileMainChampion[]
  champions: ChampionStaticListItem[]
  /** Profile slug ({gameName}-{tagLine}); drives the player-scoped links. */
  nameTag?: string
  /** Where a row leads, in place of the player-scoped champion page. */
  linkTo?: (main: ProfileMainChampion) => RouteLocationRaw
  title?: string
}>(), {
  nameTag: '',
  linkTo: undefined,
  title: 'Main champions',
})

defineSlots<{
  /** The line under the name, after the lane icon. Defaults to the sample count. */
  meta?: (props: { main: ProfileMainChampion }) => unknown
  /** The figure on the right. Defaults to the play rate. */
  value?: (props: { main: ProfileMainChampion }) => unknown
}>()

const championsById = computed(() => new Map(props.champions.map(c => [c.championId, c])))

function championName(championId: number): string {
  return championsById.value.get(championId)?.name ?? `Champion ${championId}`
}

// The count is a share of the recent-ranked window the main analysis reads, not
// the player's whole history on the champion — and the champion page one row
// away counts that whole history, for one patch and lane. Two bare counts that
// disagree read as a bug; naming this one's denominator is what tells them
// apart (#1498). `sampleMatches` comes from the API, so nothing here invents a
// window: rows written before it existed fall back to the plain count.
function formatSample(main: ProfileMainChampion): string {
  return main.sampleMatches > 0
    ? `${main.games} of last ${main.sampleMatches}`
    : `${main.games} games`
}

// Mains whose games have aged out of retention (#1216). The numbers stay — they
// are a real past measurement, and hiding them would drop the player off their
// own profile — but they get a marker, because an unqualified count here is what
// let this card promise "10 games on Graves" over a champion page holding
// nothing. The qualifier is the warning glyph and its tooltip only: the inline
// `as of 1 Aug` that used to sit beside the count was dropped in #1275.
function retiredNote(main: ProfileMainChampion): string | null {
  return main.isSampleRetired ? retiredSampleTooltip(main.measuredAtUtc) : null
}

// Drill into how THIS player builds the champion (player-scoped page), not the
// global meta. The player slug is already URL-shaped; `truemainPathFor` encodes
// it so names with reserved characters round-trip.
const { truemainPathFor } = useChampionSlugs()

function championLink(main: ProfileMainChampion): RouteLocationRaw {
  return props.linkTo?.(main) ?? truemainPathFor(props.nameTag, main.championId)
}
</script>

<template>
  <section v-if="mains.length > 0" class="flex flex-col gap-2">
    <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
      {{ title }}
    </h2>
    <!-- The surface and the clip used to need separate elements: `glass` mixed
         `backdrop-filter` with `overflow-hidden` into a WebKit bug that bled the
         blur past the rounded corners. `surface` is opaque, so both can sit on
         the list itself and the wrapper is gone. -->
    <ul class="surface flex flex-col divide-y divide-default/40 overflow-hidden rounded-lg">
      <li
        v-for="main in mains"
        :key="main.championId"
      >
        <ProfileMainChampionRow
          :to="championLink(main)"
          :icon-url="championsById.get(main.championId)?.iconUrl ?? null"
          :name="championName(main.championId)"
          :position="main.primaryPosition"
          :is-otp="main.isOtp"
          :retired-note="retiredNote(main)"
        >
          <template #meta>
            <slot name="meta" :main="main">
              {{ formatSample(main) }}
            </slot>
          </template>
          <slot name="value" :main="main">
            {{ formatPercentage(main.playRate, 0) }}
          </slot>
        </ProfileMainChampionRow>
      </li>
    </ul>
  </section>
</template>

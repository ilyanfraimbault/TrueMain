<script setup lang="ts">
import { championDisplayName } from '#shared/utils/champion-name'
import type { RouteLocationRaw } from 'vue-router'
import type { ProfileMainChampion } from '#shared/types/profile'
import type { ChampionStaticListItem } from '#shared/types/static-data'
import { formatPercentage } from '#shared/utils/ddragon'
import { retiredSampleTooltip } from '#common/utils/retired-sample'

// The champions a player plays, most played first. On a profile each row reads
// the main analysis (its play rate) and opens how THIS player builds the
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
  /** The line under the name, after the lane icon. Empty by default: the play rate says it. */
  meta?: (props: { main: ProfileMainChampion }) => unknown
  /** The figure on the right. Defaults to the play rate. */
  value?: (props: { main: ProfileMainChampion }) => unknown
}>()

const championsById = computed(() => new Map(props.champions.map(c => [c.championId, c])))

function championName(championId: number): string {
  return championDisplayName(championId, championsById.value.get(championId))
}

// Mains whose games have aged out of retention (#1216). The numbers stay — they
// are a real past measurement, and hiding them would drop the player off their
// own profile — but they get a marker, because an unqualified figure here is what
// let this card promise "10 games on Graves" over a champion page holding
// nothing. The qualifier is the warning dot and its tooltip only.
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
            <slot name="meta" :main="main" />
          </template>
          <slot name="value" :main="main">
            {{ formatPercentage(main.playRate, 0) }}
          </slot>
        </ProfileMainChampionRow>
      </li>
    </ul>
  </section>
</template>

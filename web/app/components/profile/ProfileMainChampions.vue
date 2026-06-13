<script setup lang="ts">
import type { ProfileMainChampion } from '~~/shared/types/profile'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { getPositionIconUrl } from '~~/shared/utils/ddragon'

const props = defineProps<{
  mains: ProfileMainChampion[]
  champions: ChampionStaticListItem[]
  /** Profile slug ({gameName}-{tagLine}); drives the player-scoped links. */
  nameTag: string
}>()

function lookupChampionIcon(championId: number): string | null {
  return props.champions.find(c => c.championId === championId)?.iconUrl ?? null
}

function lookupChampionName(championId: number): string {
  return props.champions.find(c => c.championId === championId)?.name ?? `Champion ${championId}`
}

function formatPlayRate(rate: number): string {
  return `${Math.round(rate * 100)}%`
}

// Drill into how THIS player builds the champion (player-scoped page), not the
// global meta. The whole row is the link target — so we render a plain icon
// here rather than <ChampionLink> (whose own <a> would nest inside this one,
// which is invalid HTML and would also point at the global page). The slug is
// already URL-shaped; encode it so names with reserved characters round-trip.
function championLink(championId: number) {
  return `/truemains/${encodeURIComponent(props.nameTag)}/champions/${championId}`
}
</script>

<template>
  <section v-if="mains.length > 0" class="flex flex-col gap-2">
    <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
      Main champions
    </h2>
    <!-- `glass` (backdrop-filter) and `overflow-hidden` must sit on separate
         elements: together they hit a WebKit bug that bleeds the blur past the
         rounded corners. The outer div carries the glass + radius; the inner
         list clips its row hovers to that radius. -->
    <div class="glass rounded-lg">
      <ul class="flex flex-col divide-y divide-default/40 overflow-hidden rounded-lg">
        <li
          v-for="main in mains"
          :key="main.championId"
        >
          <NuxtLink
            :to="championLink(main.championId)"
            class="glass-hover flex items-center gap-3 px-3 py-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-primary"
          >
            <SkeletonImage
              :src="lookupChampionIcon(main.championId)"
              :alt="lookupChampionName(main.championId)"
              :title="lookupChampionName(main.championId)"
              class="size-9 shrink-0 rounded"
            />
            <div class="flex min-w-0 flex-1 flex-col">
              <div class="flex items-center gap-1.5">
                <span class="truncate text-sm font-medium">
                  {{ lookupChampionName(main.championId) }}
                </span>
                <span
                  v-if="main.isOtp"
                  class="inline-flex items-center rounded-full bg-amber-400/25 px-1.5 py-0.5 text-[9px] font-bold uppercase tracking-wide text-amber-200 ring-1 ring-amber-400/50"
                  title="One-trick pony"
                >
                  OTP
                </span>
              </div>
              <div class="flex items-center gap-1 text-[11px] text-muted tabular-nums">
                <NuxtImg
                  v-if="main.primaryPosition"
                  :src="getPositionIconUrl(main.primaryPosition)"
                  :alt="main.primaryPosition"
                  class="size-3"
                  width="12"
                  height="12"
                />
                <span>{{ main.games }} games</span>
              </div>
            </div>
            <span class="shrink-0 text-sm font-semibold tabular-nums text-default">
              {{ formatPlayRate(main.playRate) }}
            </span>
          </NuxtLink>
        </li>
      </ul>
    </div>
  </section>
</template>

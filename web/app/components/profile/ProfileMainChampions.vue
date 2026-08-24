<script setup lang="ts">
import type { ProfileMainChampion } from '~~/shared/types/profile'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { formatPercentage, getPositionIconUrl } from '~~/shared/utils/ddragon'

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
  return formatPercentage(rate, 0)
}

// Mains whose games have aged out of retention (#1216). The numbers stay — they
// are a real past measurement, and hiding them would drop the player off their
// own profile — but they get dated, because an undated count here is what let
// this card promise "10 games on Graves" over a champion page holding nothing.
// Keyed by champion id so the template stays declarative.
const retiredByChampion = computed(() => new Map(
  props.mains
    .filter(main => main.isSampleRetired)
    .map(main => [main.championId, formatRetiredSample(main.measuredAtUtc)] as const)
    .filter(([, note]) => note !== null),
))

// Drill into how THIS player builds the champion (player-scoped page), not the
// global meta. The whole row is the link target — so we render a plain icon
// here rather than <ChampionLink> (whose own <a> would nest inside this one,
// which is invalid HTML and would also point at the global page). The player
// slug is already URL-shaped; `truemainPathFor` encodes it so names with
// reserved characters round-trip.
const { truemainPathFor } = useChampionSlugs()

function championLink(championId: number) {
  return truemainPathFor(props.nameTag, championId)
}

// Plain <img> + a URL built here instead of <NuxtImg> — same `_ipx/…` URL,
// minus the responsive srcset machinery a fixed 12px icon never needed. See
// SkeletonImage.vue for the profiling rationale. The URL comes from the shared
// helper so the glyph shares one cache entry across every size it is shown at.
const canonicalIcon = useCanonicalIcon()
</script>

<template>
  <section v-if="mains.length > 0" class="flex flex-col gap-2">
    <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
      Main champions
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
        <NuxtLink
          :to="championLink(main.championId)"
          class="surface-hover flex items-center gap-3 px-3 py-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-primary"
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
              <!-- Same idiom as the champion header's thin-sample qualifier: the
                   explanation lives in the tooltip so it never crowds the row. -->
              <UTooltip
                v-if="retiredByChampion.get(main.championId)"
                :text="retiredByChampion.get(main.championId)!.tooltip"
                :delay-duration="150"
              >
                <UIcon
                  name="i-lucide-triangle-alert"
                  class="size-3.5 shrink-0 text-warning"
                />
              </UTooltip>
            </div>
            <div class="flex items-center gap-1 text-[11px] text-muted tabular-nums">
              <img
                v-if="main.primaryPosition"
                :src="canonicalIcon(getPositionIconUrl(main.primaryPosition))"
                :alt="main.primaryPosition"
                class="size-3"
                width="12"
                height="12"
              >
              <span>{{ main.games }} games</span>
              <span v-if="retiredByChampion.get(main.championId)" class="text-dimmed">
                · {{ retiredByChampion.get(main.championId)!.suffix }}
              </span>
            </div>
          </div>
          <span class="shrink-0 text-sm font-semibold tabular-nums text-default">
            {{ formatPlayRate(main.playRate) }}
          </span>
        </NuxtLink>
      </li>
    </ul>
  </section>
</template>

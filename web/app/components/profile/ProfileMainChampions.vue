<script setup lang="ts">
import type { ProfileMainChampion } from '~~/shared/types/profile'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { getPositionIconUrl } from '~~/shared/utils/ddragon'

const props = defineProps<{
  mains: ProfileMainChampion[]
  champions: ChampionStaticListItem[]
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
</script>

<template>
  <section v-if="mains.length > 0" class="flex flex-col gap-2">
    <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
      Main champions
    </h2>
    <ul class="flex flex-col divide-y divide-default/40 overflow-hidden rounded-lg bg-elevated/40">
      <li
        v-for="main in mains"
        :key="main.championId"
        class="flex items-center gap-3 px-3 py-2"
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
              class="inline-flex items-center rounded-full bg-emerald-500/25 px-1.5 py-0.5 text-[9px] font-bold uppercase tracking-wide text-emerald-200 ring-1 ring-emerald-500/50"
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
        <span class="shrink-0 text-sm font-semibold tabular-nums text-emerald-300">
          {{ formatPlayRate(main.playRate) }}
        </span>
      </li>
    </ul>
  </section>
</template>

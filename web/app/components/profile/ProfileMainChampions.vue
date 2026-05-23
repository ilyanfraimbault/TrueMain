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
  <section v-if="mains.length > 0" class="flex flex-col gap-3">
    <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">
      Main champions
    </h2>
    <div class="flex gap-3 overflow-x-auto pb-1">
      <div
        v-for="main in mains"
        :key="main.championId"
        class="flex w-32 shrink-0 flex-col items-center gap-1.5 rounded-lg bg-elevated/40 p-3"
      >
        <div class="relative">
          <SkeletonImage
            :src="lookupChampionIcon(main.championId)"
            :alt="lookupChampionName(main.championId)"
            :title="lookupChampionName(main.championId)"
            class="size-16 rounded"
          />
          <span
            v-if="main.isOtp"
            class="absolute -top-1 -right-1 inline-flex items-center rounded-full bg-emerald-500/30 px-1.5 py-0.5 text-[10px] font-bold uppercase tracking-wide text-emerald-200 ring-1 ring-emerald-400/50"
            title="One-trick pony"
          >
            OTP
          </span>
        </div>
        <p class="line-clamp-1 text-center text-sm font-semibold">
          {{ lookupChampionName(main.championId) }}
        </p>
        <div class="flex items-center gap-1 text-xs text-muted">
          <NuxtImg
            v-if="main.primaryPosition"
            :src="getPositionIconUrl(main.primaryPosition)"
            :alt="main.primaryPosition"
            class="size-3.5"
            width="14"
            height="14"
          />
          <span class="tabular-nums">{{ main.games }} games</span>
        </div>
        <p class="text-xs font-semibold text-emerald-300 tabular-nums">
          {{ formatPlayRate(main.playRate) }} PR
        </p>
      </div>
    </div>
  </section>
</template>

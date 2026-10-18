<script setup lang="ts">
import { formatPercentage, getPositionIconUrl } from '#shared/utils/ddragon'
import { POSITIONS, positionLabel } from '#common/utils/positions'

/**
 * The patch at a glance, drawn like the site's "Strongest picks" panel: one
 * column per lane on the site's tier list, its best pick over its loading art
 * and the next four under it with their tier and win rate. A champion
 * opens its builds.
 */
const { laneEntries, status } = useTierList()
const { nameOf, portraitOf } = useChampionStatics()

const columns = computed(() => POSITIONS.map((lane) => {
  const [lead, ...rest] = laneEntries(lane).slice(0, 5)
  return { lane, label: positionLabel(lane), lead: lead ?? null, rest }
}))
const loading = computed(() => status.value !== 'ready' && columns.value.every(column => !column.lead))
</script>

<template>
  <div class="grid grid-cols-5 gap-3">
    <div v-for="column in columns" :key="column.lane" class="surface flex flex-col overflow-hidden rounded-2xl">
      <NuxtLink
        v-if="column.lead"
        :to="`/champions/${column.lead.championId}?position=${column.lane}`"
        class="group relative flex h-44 flex-col justify-between p-3 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-primary"
      >
        <ChampionArt :champion-id="column.lead.championId" kind="loading" fade="y" position="50% 18%" class="transition-transform duration-300 group-hover:scale-[1.03]" />
        <span class="relative flex w-fit items-center gap-1.5 rounded-full bg-ink-950/70 py-0.5 pr-2 pl-1 ring-1 ring-white/10 backdrop-blur-sm">
          <img :src="getPositionIconUrl(column.lane)" alt="" class="size-4">
          <span class="text-[11px] font-semibold uppercase tracking-wide text-default">{{ column.label }}</span>
        </span>
        <span class="relative flex items-end justify-between gap-2">
          <span class="min-w-0 leading-tight">
            <span class="block truncate text-base font-semibold text-highlighted drop-shadow-[0_1px_2px_rgb(0_0_0/0.8)]">{{ nameOf(column.lead.championId) }}</span>
            <span class="text-xs font-semibold tabular-nums" :class="winRateTone(column.lead.winRate)">{{ formatPercentage(column.lead.winRate) }} WR</span>
          </span>
          <TierBadge :tier="column.lead.tier" stacked />
        </span>
      </NuxtLink>
      <USkeleton v-else-if="loading" class="h-44 rounded-none" />
      <div v-else class="flex h-44 flex-col justify-between p-3">
        <span class="flex items-center gap-1.5">
          <img :src="getPositionIconUrl(column.lane)" alt="" class="size-4">
          <span class="text-[11px] font-semibold uppercase tracking-wide text-muted">{{ column.label }}</span>
        </span>
        <span class="text-xs text-dimmed">Not enough games yet.</span>
      </div>

      <ul class="flex flex-col py-1">
        <li v-for="entry in column.rest" :key="entry.championId">
          <NuxtLink
            :to="`/champions/${entry.championId}?position=${column.lane}`"
            class="flex items-center gap-2.5 px-3 py-1.5 transition-colors hover:bg-accented focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-primary"
          >
            <img v-if="portraitOf(entry.championId)" :src="portraitOf(entry.championId)!" alt="" class="img-skeleton size-8 shrink-0 rounded-md">
            <span class="min-w-0 flex-1 leading-tight">
              <span class="block truncate text-[13px] font-medium text-highlighted">{{ nameOf(entry.championId) }}</span>
              <span class="text-[11px] font-semibold tabular-nums" :class="winRateTone(entry.winRate)">{{ formatPercentage(entry.winRate) }}</span>
            </span>
            <TierBadge :tier="entry.tier" />
          </NuxtLink>
        </li>
        <template v-if="loading">
          <li v-for="index in 4" :key="index" class="flex items-center gap-2 px-3 py-1.5">
            <USkeleton class="size-8 rounded-md" />
            <USkeleton class="h-3 flex-1" />
          </li>
        </template>
      </ul>
    </div>
  </div>
</template>

<script setup lang="ts">
import { formatPercentage, getPositionIconUrl } from '#shared/utils/ddragon'
import { POSITIONS, positionLabel } from '#common/utils/positions'

/**
 * The patch at a glance: the three best champions of each lane on the site's
 * tier list, with their tier and win rate. A champion opens its builds.
 */
const { laneEntries, status } = useTierList()
const { nameOf, portraitOf } = useChampionStatics()

const columns = computed(() => POSITIONS.map(lane => ({ lane, top: laneEntries(lane).slice(0, 3) })))
</script>

<template>
  <div class="grid grid-cols-5 gap-3">
    <div v-for="column in columns" :key="column.lane" class="surface flex flex-col gap-2 rounded-xl p-3">
      <img :src="getPositionIconUrl(column.lane)" :alt="positionLabel(column.lane, { short: true })" :title="positionLabel(column.lane, { short: true })" class="size-5">
      <NuxtLink
        v-for="entry in column.top"
        :key="entry.championId"
        :to="`/champions/${entry.championId}?position=${column.lane}`"
        class="-mx-1 flex items-center gap-2 rounded-lg px-1 py-1 transition-colors hover:bg-accented"
      >
        <img v-if="portraitOf(entry.championId)" :src="portraitOf(entry.championId)!" alt="" class="img-skeleton size-8 rounded-md">
        <span class="min-w-0 flex-1 leading-tight">
          <span class="block truncate text-[13px] font-semibold text-highlighted">{{ nameOf(entry.championId) }}</span>
          <span class="text-[11px] font-medium tabular-nums" :class="winRateTone(entry.winRate)">{{ formatPercentage(entry.winRate) }}</span>
        </span>
        <TierBadge :tier="entry.tier" />
      </NuxtLink>
      <template v-if="status === 'pending' && !column.top.length">
        <USkeleton v-for="index in 3" :key="index" class="h-10 w-full" />
      </template>
    </div>
  </div>
</template>

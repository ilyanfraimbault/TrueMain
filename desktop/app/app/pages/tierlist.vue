<script setup lang="ts">
import type { Lane } from '~/types/draft'
import { LANE_LABELS, laneIconUrl } from '~/types/draft'

/**
 * The site's tier list — the same S/A/B/C/D per lane, computed server-side —
 * as a table the width of the window. A row opens the champion's builds on
 * that lane.
 */
const { entries, patch, status } = useTierList()
const { nameOf, portraitOf } = useChampionStatics()

const lane = ref<Lane | null>(null)
const search = ref('')

const rows = computed(() => {
  const query = search.value.trim().toLowerCase()
  return entries.value
    .filter(entry => lane.value === null || entry.position === lane.value)
    .filter(entry => !query || nameOf(entry.championId).toLowerCase().includes(query))
})

const percent = (value: number) => `${(value * 100).toFixed(1)}%`
</script>

<template>
  <div class="flex h-full flex-col gap-4 p-6">
    <PageHeader title="Tier list" icon="i-lucide-trending-up">
      <span v-if="patch" class="stat-label">Patch {{ patch }}</span>
    </PageHeader>

    <div class="flex items-center gap-3">
      <RolePicker v-model:position="lane" />
      <UInput v-model="search" icon="i-lucide-search" placeholder="Search a champion" size="sm" class="ml-auto w-56" />
    </div>

    <div class="surface flex min-h-0 flex-1 flex-col overflow-hidden rounded-xl">
      <div class="grid grid-cols-[3rem_minmax(0,1fr)_4rem_4rem_repeat(4,5.5rem)] items-center gap-2 border-b border-default px-4 py-2.5">
        <span class="stat-label">#</span>
        <span class="stat-label">Champion</span>
        <span class="stat-label text-center">Lane</span>
        <span class="stat-label text-center">Tier</span>
        <span class="stat-label text-right">Win rate</span>
        <span class="stat-label text-right">Pick rate</span>
        <span class="stat-label text-right">Ban rate</span>
        <span class="stat-label text-right">Games</span>
      </div>

      <div class="min-h-0 flex-1 overflow-y-auto">
        <NuxtLink
          v-for="(entry, index) in rows"
          :key="`${entry.championId}-${entry.position}`"
          :to="`/champions/${entry.championId}?lane=${entry.position}`"
          class="grid grid-cols-[3rem_minmax(0,1fr)_4rem_4rem_repeat(4,5.5rem)] items-center gap-2 border-b border-default/60 px-4 py-2 transition-colors hover:bg-accented"
        >
          <span class="text-sm tabular-nums text-dimmed">{{ index + 1 }}</span>
          <span class="flex min-w-0 items-center gap-3">
            <img v-if="portraitOf(entry.championId)" :src="portraitOf(entry.championId)!" alt="" class="size-8 rounded-md">
            <span class="truncate text-sm font-semibold text-highlighted">{{ nameOf(entry.championId) }}</span>
          </span>
          <img :src="laneIconUrl(entry.position)" :alt="LANE_LABELS[entry.position as Lane]" :title="LANE_LABELS[entry.position as Lane]" class="mx-auto size-5">
          <span class="flex justify-center"><TierBadge :tier="entry.tier" /></span>
          <span class="text-right text-sm font-semibold tabular-nums" :class="winRateTone(entry.winRate)">{{ percent(entry.winRate) }}</span>
          <span class="text-right text-sm tabular-nums text-default">{{ percent(entry.pickRate) }}</span>
          <span class="text-right text-sm tabular-nums text-default">{{ percent(entry.banRate) }}</span>
          <span class="text-right text-sm tabular-nums text-muted">{{ entry.games.toLocaleString('en-US') }}</span>
        </NuxtLink>

        <div v-if="status === 'pending' && !rows.length" class="flex flex-col gap-2 p-4">
          <USkeleton v-for="index in 8" :key="index" class="h-10 w-full" />
        </div>
        <p v-else-if="!rows.length" class="p-8 text-center text-sm text-muted">
          {{ status === 'error' ? 'The tier list could not be loaded' : 'No champion matches' }}
        </p>
      </div>
    </div>
  </div>
</template>

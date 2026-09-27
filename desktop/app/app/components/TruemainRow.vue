<script setup lang="ts">
import type { TruemainRow } from '~/types/truemains'
import { formatRank, profilePath, rankCrestUrl } from '~/types/truemains'
import { laneIconUrl } from '~/types/draft'

/**
 * One true main: rank, who, their ranked standing and season record, the
 * champions they main. The row opens their page on the site; the star keeps
 * them in this app's favorites.
 */
const props = defineProps<{ row: TruemainRow, rank?: number | null }>()

const { profileIconOf, portraitOf, nameOf } = useChampionStatics()
const { isFavorite, toggle } = useFavorites()

const record = computed(() => {
  const { wins, losses } = props.row.stats
  const total = wins + losses
  return total > 0 ? { wins, losses, rate: wins / total } : null
})
</script>

<template>
  <div class="group grid grid-cols-[2.5rem_minmax(0,1fr)_10rem_7.5rem_9rem_2rem] items-center gap-3 border-b border-default/60 px-4 py-2 transition-colors hover:bg-accented">
    <span class="text-sm font-semibold tabular-nums text-dimmed">{{ rank ?? row.rank }}</span>

    <button type="button" class="flex min-w-0 items-center gap-3 text-left" :title="`Open ${row.identity.gameName} on truemain.lol`" @click="openOnSite(profilePath(row.identity))">
      <img :src="profileIconOf(row.identity.profileIconId) ?? undefined" alt="" class="size-9 shrink-0 rounded-full bg-ink-800 ring-1 ring-default">
      <span class="min-w-0 leading-tight">
        <span class="block truncate text-sm font-semibold text-highlighted group-hover:underline">
          {{ row.identity.gameName }}<span v-if="row.identity.tagLine" class="font-normal text-dimmed"> #{{ row.identity.tagLine }}</span>
        </span>
        <span class="mt-0.5 flex items-center gap-1.5 text-[11px] text-dimmed">
          <img v-if="row.positions.primary" :src="laneIconUrl(row.positions.primary)" alt="" class="size-3.5 opacity-70">
          {{ row.identity.platformId }}
        </span>
      </span>
    </button>

    <span class="flex items-center gap-2 text-xs text-default">
      <img v-if="rankCrestUrl(row.ranked.tier)" :src="rankCrestUrl(row.ranked.tier)!" alt="" class="size-6">
      {{ formatRank(row.ranked) }}
    </span>

    <span v-if="record" class="flex flex-col leading-tight">
      <span class="text-sm font-semibold tabular-nums" :class="winRateTone(record.rate)">{{ (record.rate * 100).toFixed(1) }}%</span>
      <span class="text-[11px] tabular-nums text-dimmed">{{ record.wins }}W {{ record.losses }}L</span>
    </span>
    <span v-else class="text-xs text-dimmed">—</span>

    <span class="flex items-center gap-1">
      <NuxtLink v-for="champion in row.topChampions.slice(0, 3)" :key="champion.championId" :to="`/champions/${champion.championId}`" :title="`${nameOf(champion.championId)} · ${champion.games} games`">
        <img v-if="portraitOf(champion.championId)" :src="portraitOf(champion.championId)!" :alt="nameOf(champion.championId)" class="size-7 rounded-md ring-1 ring-default hover:ring-primary/60">
      </NuxtLink>
    </span>

    <UButton
      icon="i-lucide-star"
      color="neutral"
      variant="ghost"
      size="xs"
      :class="isFavorite(row.identity) ? 'text-gold' : 'text-dimmed opacity-0 group-hover:opacity-100'"
      :aria-label="isFavorite(row.identity) ? 'Remove from favorites' : 'Add to favorites'"
      :aria-pressed="isFavorite(row.identity)"
      @click="toggle(row.identity)"
    />
  </div>
</template>

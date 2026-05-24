<script setup lang="ts">
import type { LeaderboardRowResponse } from '~~/shared/types/leaderboard'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { getProfileIconUrl } from '~~/shared/utils/ddragon'
import { isApexTier } from '~/utils/tiers'

// One row of the leaderboard. The whole row is a NuxtLink to the profile so
// keyboard / screen-reader navigation lands on a single focusable target
// rather than scattering tab stops across the cells.
const props = defineProps<{
  row: LeaderboardRowResponse
  championsById: Map<number, ChampionStaticListItem>
  patch: string | null
}>()

const profileHref = computed(() => {
  const tag = props.row.identity.tagLine
  return tag
    ? `/truemains/${encodeURIComponent(`${props.row.identity.gameName}-${tag}`)}`
    : `/truemains/${encodeURIComponent(props.row.identity.gameName)}`
})

const profileIconUrl = computed(() =>
  getProfileIconUrl(props.row.identity.profileIconId, props.patch))

const ranked = computed(() => props.row.ranked)
const showDivision = computed(() => ranked.value !== null && !isApexTier(ranked.value.tier))

const winRateLabel = computed(() => {
  const wr = props.row.stats.winRate
  return wr === null ? null : `${Math.round(wr * 100)}%`
})
const kdaLabel = computed(() => {
  const kda = props.row.stats.kda
  return kda === null ? null : kda.toFixed(1)
})

function championName(id: number): string {
  return props.championsById.get(id)?.name ?? `#${id}`
}
function championIcon(id: number): string | null {
  return props.championsById.get(id)?.iconUrl ?? null
}
</script>

<template>
  <NuxtLink
    :to="profileHref"
    class="group flex items-center gap-3 rounded-md border border-default/60 bg-elevated/40 px-3 py-2 transition-colors hover:bg-elevated/80 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
  >
    <!-- Rank -->
    <span class="w-10 shrink-0 text-center text-sm font-semibold tabular-nums text-muted">
      #{{ row.rank }}
    </span>

    <!-- Region flag (sits before avatar so the eye picks up region at a glance). -->
    <LeaderboardRegionFlag :region="row.region" :width="24" />

    <!-- Avatar -->
    <SkeletonImage
      v-if="profileIconUrl"
      :src="profileIconUrl"
      :alt="row.identity.gameName"
      class="size-10 shrink-0 rounded"
      width="40"
      height="40"
    />
    <div v-else class="size-10 shrink-0 rounded bg-elevated/60" aria-hidden="true" />

    <!-- Name + tag + level -->
    <div class="min-w-0 flex-1">
      <div class="flex items-baseline gap-1 truncate">
        <span class="truncate font-semibold text-default">{{ row.identity.gameName }}</span>
        <span v-if="row.identity.tagLine" class="text-xs text-muted">#{{ row.identity.tagLine }}</span>
      </div>
      <div class="text-xs text-muted">
        Lvl {{ row.identity.summonerLevel }}
      </div>
    </div>

    <!-- Rank emblem + LP. Division is shown as a small Roman numeral after
         the emblem for non-apex tiers (Master+ ignore division). -->
    <div v-if="ranked" class="flex w-28 shrink-0 items-center justify-end gap-2">
      <RankIcon :tier="ranked.tier" :size="28" />
      <div class="flex flex-col items-end leading-tight">
        <span v-if="showDivision" class="text-[10px] uppercase tracking-wide text-muted">
          {{ ranked.division }}
        </span>
        <span class="text-sm font-semibold tabular-nums text-default">
          {{ ranked.leaguePoints }} LP
        </span>
      </div>
    </div>

    <!-- Top 3 champions -->
    <div class="hidden shrink-0 items-center gap-1 md:flex">
      <template v-for="champ in row.topChampions" :key="champ.championId">
        <SkeletonImage
          v-if="championIcon(champ.championId)"
          :src="championIcon(champ.championId)!"
          :alt="championName(champ.championId)"
          :title="`${championName(champ.championId)} · ${champ.games} games`"
          class="size-7 rounded"
          width="28"
          height="28"
        />
        <div
          v-else
          class="size-7 rounded bg-elevated/60"
          :title="`#${champ.championId} · ${champ.games} games`"
          aria-hidden="true"
        />
      </template>
      <!-- Fill empty slots so the row keeps its width when a player has < 3 mains. -->
      <div
        v-for="i in Math.max(0, 3 - row.topChampions.length)"
        :key="`fill-${i}`"
        class="size-7 rounded bg-elevated/30"
        aria-hidden="true"
      />
    </div>

    <!-- Games / KDA / WR -->
    <div class="ml-auto hidden shrink-0 items-center gap-5 sm:flex">
      <div class="flex w-12 flex-col items-end">
        <span class="text-sm font-semibold tabular-nums text-default">{{ row.stats.games.toLocaleString() }}</span>
        <span class="text-[10px] uppercase tracking-wide text-muted">games</span>
      </div>
      <div v-if="kdaLabel !== null" class="flex w-12 flex-col items-end">
        <span class="text-sm font-semibold tabular-nums text-default">{{ kdaLabel }}</span>
        <span class="text-[10px] uppercase tracking-wide text-muted">kda</span>
      </div>
      <div v-if="winRateLabel !== null" class="flex w-12 flex-col items-end">
        <span class="text-sm font-semibold tabular-nums text-default">{{ winRateLabel }}</span>
        <span class="text-[10px] uppercase tracking-wide text-muted">wr</span>
      </div>
    </div>
  </NuxtLink>
</template>

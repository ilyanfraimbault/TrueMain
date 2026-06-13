<script setup lang="ts">
import type { LeaderboardRowResponse } from '~~/shared/types/leaderboard'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { getProfileIconUrl } from '~~/shared/utils/ddragon'
import { isApexTier } from '~/utils/tiers'

// One row of the leaderboard. The whole row navigates to the player's profile
// via a stretched overlay link, while the top-champion icons are their own
// links to that champion's player-scoped build page — siblings of the overlay,
// never nested <a> inside <a>.
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

// Slug for this player's truemain pages — `{gameName}-{tagLine}` (or just the
// name when untagged). Drives the player-scoped champion links below; the slug
// is URL-encoded by <ChampionLink>.
const rowNameTag = computed(() => {
  const { gameName, tagLine } = props.row.identity
  return tagLine ? `${gameName}-${tagLine}` : gameName
})

// The stretched profile link is an empty overlay (no text), so it needs an
// explicit accessible name.
const profileAriaLabel = computed(() => {
  const { gameName, tagLine } = props.row.identity
  return tagLine ? `${gameName} #${tagLine}` : gameName
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

// Share of tracked games spent on the #1 champion — the "play rate" shown
// next to the main pick on dpm/op.gg leaderboards. Clamped because a filtered
// `stats.games` can occasionally trail the per-champion total.
const mainPlayRate = computed(() => {
  const main = props.row.topChampions[0]
  if (!main || props.row.stats.games <= 0) return null
  return Math.min(100, Math.round((main.games / props.row.stats.games) * 100))
})

function championName(id: number): string {
  return props.championsById.get(id)?.name ?? `#${id}`
}
function championIcon(id: number): string | null {
  return props.championsById.get(id)?.iconUrl ?? null
}
</script>

<template>
  <div
    class="group relative flex items-center gap-3 rounded-md border border-default/60 bg-elevated/40 px-3 py-2 transition-colors hover:bg-elevated/80"
  >
    <!-- Stretched profile link: a sibling overlay (not a wrapper) so the
         champion icons can be their own links without nesting <a> in <a>.
         Static content falls through to it; the top-champion links opt out
         with `relative z-10`. -->
    <NuxtLink
      :to="profileHref"
      :aria-label="profileAriaLabel"
      class="absolute inset-0 z-[1] rounded-md focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
    />
    <!-- Rank -->
    <span class="w-10 shrink-0 text-center text-sm font-semibold tabular-nums text-muted">
      #{{ row.rank }}
    </span>

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

    <!-- Name + tag, region flag sits under the name as a small badge. -->
    <div class="min-w-0 flex-1">
      <div class="flex items-baseline gap-1 truncate">
        <span class="truncate font-bold text-default">{{ row.identity.gameName }}</span>
        <span v-if="row.identity.tagLine" class="text-xs text-muted">#{{ row.identity.tagLine }}</span>
      </div>
      <LeaderboardRegionFlag :region="row.region" :width="18" class="mt-0.5" />
    </div>

    <!-- Rank emblem. Division is shown as a small Roman numeral next to
         the icon for non-apex tiers (Master+ ignore division). LP is
         intentionally omitted — rows are already sorted by it, the number
         itself was noise. -->
    <div v-if="ranked" class="flex shrink-0 items-center gap-1.5">
      <RankIcon :tier="ranked.tier" :size="28" />
      <span v-if="showDivision" class="text-xs uppercase tracking-wide text-muted">
        {{ ranked.division }}
      </span>
    </div>

    <!-- Top champions. The #1 pick is shown larger with its play rate (the
         dpm/op.gg "this is what they main" cue); the next two ride alongside
         as smaller icons. No placeholders when the player has no
         main-champion stats — the column simply collapses and the right
         stats block slides left, which keeps the row visually honest. -->
    <div
      v-if="row.topChampions.length > 0"
      class="relative z-10 hidden shrink-0 items-center gap-2 md:flex"
    >
      <div class="flex items-center gap-1.5">
        <ChampionLink
          v-if="championIcon(row.topChampions[0]!.championId)"
          :champion-id="row.topChampions[0]!.championId"
          :name="championName(row.topChampions[0]!.championId)"
          :icon-url="championIcon(row.topChampions[0]!.championId)"
          :name-tag="rowNameTag"
          :title="`${championName(row.topChampions[0]!.championId)} · ${row.topChampions[0]!.games} games`"
          class="size-9"
        />
        <div
          v-else
          class="size-9 rounded bg-elevated/60"
          :title="`#${row.topChampions[0]!.championId} · ${row.topChampions[0]!.games} games`"
          aria-hidden="true"
        />
        <span
          v-if="mainPlayRate !== null"
          class="w-9 text-xs tabular-nums text-muted"
        >{{ mainPlayRate }}%</span>
      </div>

      <div
        v-if="row.topChampions.length > 1"
        class="flex items-center gap-1"
      >
        <template v-for="champ in row.topChampions.slice(1)" :key="champ.championId">
          <ChampionLink
            v-if="championIcon(champ.championId)"
            :champion-id="champ.championId"
            :name="championName(champ.championId)"
            :icon-url="championIcon(champ.championId)"
            :name-tag="rowNameTag"
            :title="`${championName(champ.championId)} · ${champ.games} games`"
            class="size-6"
          />
          <div
            v-else
            class="size-6 rounded bg-elevated/60"
            :title="`#${champ.championId} · ${champ.games} games`"
            aria-hidden="true"
          />
        </template>
      </div>
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
  </div>
</template>

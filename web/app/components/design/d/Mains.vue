<script setup lang="ts">
import type { LeaderboardRowReadModel as LeaderboardRowResponse } from '#shared/contract/api'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { formatPercentage } from '~~/shared/utils/ddragon'
import { truemainNameTag, truemainProfilePath } from '~~/shared/utils/truemain-path'
import { leaderboardChampionIcon, leaderboardChampionName } from '#common/utils/leaderboard-row'
import { winRateTone } from '#common/utils/rate-tone'

// Variant D ("Cockpit"): the leaderboard's first rows as a dashboard panel —
// each player, the champion they main with its share of their games, their
// rank and win rate. The page owns the fetch (SSR, like /truemains).
const props = defineProps<{
  rows: LeaderboardRowResponse[]
  championsById: Map<number, ChampionStaticListItem>
  initialLoading: boolean
  /** Data Dragon version, for the profile icons. */
  patch: string | null
}>()

const canonicalIcon = useCanonicalIcon()

const display = computed(() => props.rows.map((row) => {
  const main = row.topChampions[0] ?? null
  return {
    row,
    key: `${row.identity.gameName}-${row.identity.tagLine}`,
    href: truemainProfilePath(truemainNameTag(row.identity.gameName, row.identity.tagLine)),
    main,
    mainName: main ? leaderboardChampionName(props.championsById, main.championId) : null,
    mainIcon: main ? canonicalIcon(leaderboardChampionIcon(props.championsById, main.championId)) : undefined,
    winRate: row.stats.winRate === null ? null : formatPercentage(row.stats.winRate, 0),
  }
}))
</script>

<template>
  <section
    class="d-mains d-panel"
    aria-labelledby="d-mains-title"
  >
    <span
      class="d-ring"
      data-d-spot
      aria-hidden="true"
    />

    <header class="d-phead">
      <div class="min-w-0">
        <h2
          id="d-mains-title"
          class="d-phead__title"
        >
          Top truemains
        </h2>
        <p class="d-phead__sub">
          The leaderboard's highest ranks
        </p>
      </div>
      <NuxtLink
        to="/truemains"
        class="d-plink d-plink--head"
      >
        Leaderboard
        <UIcon
          name="i-lucide-arrow-right"
          class="size-3.5"
        />
      </NuxtLink>
    </header>

    <div
      class="d-prow d-prow--head"
      aria-hidden="true"
    >
      <span>Player</span>
      <span class="d-prow__main">Main</span>
      <span class="text-right">Rank</span>
      <span class="text-right">WR</span>
    </div>

    <ul
      v-if="initialLoading"
      aria-hidden="true"
    >
      <li
        v-for="i in TRUEMAINS_TEASER_ROWS"
        :key="i"
        class="d-prow"
      >
        <span class="flex items-center gap-2.5">
          <USkeleton class="size-7 rounded-md" />
          <USkeleton class="h-3.5 w-28" />
        </span>
        <USkeleton class="d-prow__main h-6 w-16" />
        <USkeleton class="ml-auto h-5 w-10" />
        <USkeleton class="ml-auto h-3.5 w-8" />
      </li>
    </ul>

    <ul v-else-if="display.length > 0">
      <li
        v-for="entry in display"
        :key="entry.key"
      >
        <NuxtLink
          :to="entry.href"
          class="d-prow"
        >
          <Account
            :identity="entry.row.identity"
            :region="entry.row.region"
            :patch="patch"
            size="sm"
            :to="false"
            class="min-w-0"
          />

          <span class="d-prow__main">
            <template v-if="entry.main">
              <img
                v-if="entry.mainIcon"
                :src="entry.mainIcon"
                :alt="entry.mainName ?? ''"
                :title="entry.mainName ?? undefined"
                width="24"
                height="24"
                class="d-prow__champ img-skeleton"
              >
              <span class="d-prow__share">{{ formatPercentage(entry.main.playRate, 0) }}</span>
              <span
                v-if="entry.main.isOtp"
                class="d-prow__otp"
                title="One-trick pony"
              >OTP</span>
            </template>
          </span>

          <span class="d-prow__rank">
            <RankEmblem
              v-if="entry.row.ranked"
              :rank="entry.row.ranked"
              :record="entry.row.stats"
              :size="22"
            />
          </span>

          <span
            class="d-prow__wr"
            :class="winRateTone(entry.row.stats.winRate)"
          >{{ entry.winRate ?? '—' }}</span>
        </NuxtLink>
      </li>
    </ul>

    <UEmpty
      v-else
      size="sm"
      variant="naked"
      icon="i-lucide-trophy"
      description="No ranked truemains yet."
    />
  </section>
</template>

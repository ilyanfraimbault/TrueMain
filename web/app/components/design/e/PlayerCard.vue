<script setup lang="ts">
import type { LeaderboardRowResponse } from '~~/shared/types/leaderboard'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { formatCount } from '~~/shared/utils/counts'
import { championSplashUrlFromIcon, formatPercentage } from '~~/shared/utils/ddragon'
import { truemainNameTag, truemainProfilePath } from '~~/shared/utils/truemain-path'
import { leaderboardChampionIcon, leaderboardChampionName } from '#common/utils/leaderboard-row'
import { winRateTone } from '#common/utils/rate-tone'
import { formatRank } from '#common/utils/tiers'

// A truemain as a launcher's player card: their main champion's splash behind
// them, the profile icon and Riot ID, the rank, the main and how much of their
// play it takes, the win rate. The whole card opens the profile. It tilts a
// touch toward the cursor and a glare follows it — both read from the light
// engine's variables (`data-e-spot`), no listener of its own.
const props = defineProps<{
  row: LeaderboardRowResponse
  championsById: Map<number, ChampionStaticListItem>
  patch: string | null
  featured?: boolean
}>()

const ipx = useImage()

const main = computed(() => props.row.topChampions[0] ?? null)
const mainName = computed(() => (main.value ? leaderboardChampionName(props.championsById, main.value.championId) : null))
const mainIcon = computed(() => (main.value ? leaderboardChampionIcon(props.championsById, main.value.championId) : null))
const splash = computed(() => {
  const url = championSplashUrlFromIcon(mainIcon.value)
  return url ? ipx(url, { width: props.featured ? 960 : 560, format: 'webp' }) : null
})
const href = computed(() => truemainProfilePath(truemainNameTag(props.row.identity.gameName, props.row.identity.tagLine)))
const rank = computed(() => (props.row.ranked
  ? formatRank(props.row.ranked.tier, props.row.ranked.division, { lp: props.row.ranked.leaguePoints })
  : null))
const winRate = computed(() => props.row.stats.winRate)
</script>

<template>
  <NuxtLink
    :to="href"
    class="e-player"
    :class="{ 'e-player--featured': featured }"
    data-e-spot
  >
    <span
      class="e-player__art"
      aria-hidden="true"
    >
      <img
        v-if="splash"
        :src="splash"
        alt=""
        loading="lazy"
        decoding="async"
        class="e-player__img"
      >
    </span>
    <span
      class="e-player__glare"
      aria-hidden="true"
    />

    <span class="e-player__body">
      <span class="e-player__who">
        <Account
          :identity="row.identity"
          :region="row.region"
          :patch="patch"
          :size="featured ? 'lg' : 'md'"
          :to="false"
          loading="lazy"
        />
        <span
          v-if="row.persona"
          class="e-player__persona"
        >{{ row.persona.displayName }}</span>
      </span>

      <span class="e-player__rank">
        <template v-if="row.ranked">
          <RankIcon
            :tier="row.ranked.tier"
            :size="featured ? 30 : 24"
            loading="lazy"
          />
          <span>{{ rank }}</span>
        </template>
        <span
          v-else
          class="text-dimmed"
        >Unranked</span>
      </span>

      <span class="e-player__foot">
        <span
          v-if="main"
          class="e-player__main"
        >
          <img
            v-if="mainIcon"
            :src="mainIcon"
            alt=""
            width="28"
            height="28"
            loading="lazy"
            class="e-player__champ img-skeleton"
          >
          <span class="e-player__main-text">
            <span class="e-player__main-name">
              {{ mainName }}
              <OtpBadge v-if="main.isOtp" />
            </span>
            <span class="e-player__main-share">{{ formatPercentage(main.playRate, 0) }} of their games</span>
          </span>
        </span>
        <span
          v-if="winRate !== null"
          class="e-player__wr"
        >
          <span
            class="e-player__wr-value"
            :class="winRateTone(winRate)"
          >{{ formatPercentage(winRate, 0) }}</span>
          <span class="e-player__wr-label">{{ formatCount(row.stats.games) }} games</span>
        </span>
      </span>
    </span>
  </NuxtLink>
</template>
